const CODE_TTL = 10 * 60_000;
const SESSION_TTL = 14 * 24 * 60 * 60_000;
const MAX_CODE_ATTEMPTS = 5;
const MAIL_WINDOW = 60 * 60_000;
const SESSION_COOKIE = 'asp_session';

function cookieValue(request, name) {
  return (request.headers.get('cookie') ?? '').split(/;\s*/).find(part => part.startsWith(name + '='))?.slice(name.length + 1);
}

function sessionCookie(url, token, seconds) {
  return SESSION_COOKIE + '=' + token + '; Path=/; Max-Age=' + seconds + '; HttpOnly; SameSite=Lax'
    + (url.protocol === 'https:' ? '; Secure' : '');
}

async function readSession(db, request, desktop = false) {
  const token = desktop ? /^Bearer ([a-f0-9]{64})$/.exec(request.headers.get('authorization') ?? '')?.[1]
    : cookieValue(request, SESSION_COOKIE);
  if (!/^[a-f0-9]{64}$/.test(token ?? '')) return null;
  const hash = await digest(desktop ? 'desktop:' + token : token);
  const account = await db.prepare(
    'SELECT accounts.id, accounts.email FROM auth_sessions JOIN accounts ON accounts.id = auth_sessions.account_id'
    + ' WHERE token_hash = ? AND expires_at > ?'
  ).bind(hash, Date.now()).first();
  return account ? { ...account, hash } : null;
}

async function requireSession(db, request, desktop = false) {
  const user = await readSession(db, request, desktop);
  if (!user) throw new HttpError(401, '请先用邮箱验证码登录。', 'login_required');
  return user;
}

function normalizeEmail(value) {
  const email = typeof value === 'string' ? value.trim().toLowerCase() : '';
  const [local, host, extra] = email.split('@');
  if (email.length > 254 || local.length > 64 || extra !== undefined
      || !/^[a-z0-9_%+!#$&'*\/=?^`{|}~-]+(?:\.[a-z0-9_%+!#$&'*\/=?^`{|}~-]+)*$/.test(local)
      || !/^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/.test(host ?? '')) {
    throw new HttpError(400, '请输入有效的邮箱地址。');
  }
  return email;
}

async function hmac(secret, message) {
  const key = await crypto.subtle.importKey('raw', secret, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const bytes = await crypto.subtle.sign('HMAC', key, encode.encode(message));
  return Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0')).join('');
}

function emailCode() {
  const number = new Uint32Array(1);
  do crypto.getRandomValues(number);
  while (number[0] >= 4_294_000_000);
  return String(number[0] % 1_000_000).padStart(6, '0');
}

async function takeAuthLimit(db, key, expiresAt, max, now, message = '验证码请求过于频繁，请稍后再试。') {
  const row = await db.prepare(
    'INSERT INTO auth_limits VALUES (?, ?, 1) ON CONFLICT(key) DO UPDATE SET '
    + 'attempts = CASE WHEN auth_limits.expires_at <= ? THEN 1 ELSE attempts + 1 END, '
    + 'expires_at = CASE WHEN auth_limits.expires_at <= ? THEN excluded.expires_at ELSE auth_limits.expires_at END '
    + 'WHERE auth_limits.expires_at <= ? OR attempts < ? RETURNING attempts'
  ).bind(key, expiresAt, now, now, now, max).first();
  if (!row) throw new HttpError(429, message);
}

async function sendEmailCode(request, env) {
  const { DB: db, AUTH: auth } = env;
  const body = await readBody(request, 2048);
  if (!auth?.sendCode) throw new HttpError(503, '邮件验证码暂不可用，请稍后再试。');
  const email = normalizeEmail(body.email);
  const visitor = visitorFrom(request);
  await verifyCaptcha(db, visitor, 'login', '', body.captcha);

  const now = Date.now();
  const subject = env.clientIp || visitor;
  const emailKey = await hmac(auth.secret, 'email:' + email);
  const ipKey = await hmac(auth.secret, 'ip:' + subject);
  await db.batch([
    db.prepare('DELETE FROM auth_email_codes WHERE expires_at <= ?').bind(now),
    db.prepare('DELETE FROM auth_limits WHERE expires_at <= ?').bind(now)
  ]);
  await takeAuthLimit(db, 'cooldown:' + emailKey, now + 60_000, 1, now);
  await takeAuthLimit(db, 'mail:' + emailKey, now + MAIL_WINDOW, 5, now);
  await takeAuthLimit(db, 'ip:' + ipKey, now + MAIL_WINDOW, 15, now);
  await takeAuthLimit(db, 'global', now + MAIL_WINDOW, 120, now);

  const id = crypto.randomUUID();
  const code = emailCode();
  const hash = await hmac(auth.secret, 'code:' + id + ':' + code);
  await db.prepare(
    'INSERT INTO auth_email_codes (id, visitor, email, code_hash, created_at, expires_at) VALUES (?, ?, ?, ?, ?, ?)'
  ).bind(id, visitor, email, hash, now, now + CODE_TTL).run();
  try {
    await auth.sendCode(email, code);
  } catch (error) {
    await db.prepare('DELETE FROM auth_email_codes WHERE id = ?').bind(id).run();
    const reason = String(error?.code ?? 'provider').slice(0, 100);
    console.error('Mail delivery failed:', /^[\w.:-]+$/.test(reason) ? reason : 'provider');
    throw new HttpError(503, '验证码邮件发送失败，请稍后重试。');
  }
  await db.prepare('DELETE FROM auth_email_codes WHERE email = ? AND visitor = ? AND created_at < ?')
    .bind(email, visitor, now).run();
  return reply({ id, expiresIn: CODE_TTL / 1000, resendAfter: 60 }, 202);
}

async function verifyEmailCode(request, url, env, desktop = false) {
  const { DB: db, AUTH: auth } = env;
  if (desktop && (request.headers.has('origin') || request.headers.has('sec-fetch-site'))) {
    throw new HttpError(403, '请在 ASP Assistant 客户端内登录。');
  }
  const { id, code } = await readBody(request, 2048);
  const visitor = visitorFrom(request);
  const reject = () => new HttpError(400, '验证码不正确、已过期或尝试次数过多，请重新获取。');
  if (!auth?.secret || !validId(id) || !visitor || typeof code !== 'string' || !/^\d{6}$/.test(code)) throw reject();

  const now = Date.now();
  const hash = await hmac(auth.secret, 'code:' + id + ':' + code);
  const claimed = await db.prepare(
    'DELETE FROM auth_email_codes WHERE id = ? AND visitor = ? AND expires_at > ? AND attempts < ?'
    + ' AND code_hash = ? RETURNING email'
  ).bind(id, visitor, now, MAX_CODE_ATTEMPTS, hash).first();
  if (!claimed) {
    await db.prepare(
      'UPDATE auth_email_codes SET attempts = attempts + 1 WHERE id = ? AND visitor = ?'
      + ' AND expires_at > ? AND attempts < ?'
    ).bind(id, visitor, now, MAX_CODE_ATTEMPTS).run();
    throw reject();
  }

  const token = Array.from(random(32), byte => byte.toString(16).padStart(2, '0')).join('');
  const old = await readSession(db, request, desktop);
  const tokenHash = await digest(desktop ? 'desktop:' + token : token);
  const expiresAt = now + SESSION_TTL;
  await db.batch([
    db.prepare('INSERT INTO accounts VALUES (?, ?, ?) ON CONFLICT(email) DO NOTHING')
      .bind(crypto.randomUUID(), claimed.email, now),
    db.prepare('DELETE FROM auth_sessions WHERE expires_at <= ? OR token_hash = ?').bind(now, old?.hash ?? ''),
    db.prepare('INSERT INTO auth_sessions SELECT ?, id, ? FROM accounts WHERE email = ?')
      .bind(tokenHash, expiresAt, claimed.email)
  ]);
  const user = { email: claimed.email };
  if (desktop) return reply({ user, token, expiresAt });
  return reply({ user }, 200, { 'Set-Cookie': sessionCookie(url, token, SESSION_TTL / 1000) });
}

async function authApi(request, url, env) {
  const path = url.pathname;
  if (request.method === 'GET' && path === '/api/auth/session') {
    const session = await readSession(env.DB, request);
    return reply({ user: session ? { email: session.email } : null, mailAvailable: Boolean(env.AUTH?.sendCode) });
  }
  if (request.method === 'POST') {
    if (path === '/api/auth/code') return sendEmailCode(request, env);
    if (path === '/api/auth/verify') return verifyEmailCode(request, url, env);
    if (path === '/api/auth/desktop/verify') return verifyEmailCode(request, url, env, true);
    if (path === '/api/auth/desktop/logout') {
      await readBody(request, 2048);
      const session = await readSession(env.DB, request, true);
      if (session) await env.DB.prepare('DELETE FROM auth_sessions WHERE token_hash = ?').bind(session.hash).run();
      return reply({ user: null });
    }
    if (path === '/api/auth/logout') {
      await readBody(request, 2048);
      const session = await readSession(env.DB, request);
      if (session) await env.DB.prepare('DELETE FROM auth_sessions WHERE token_hash = ?').bind(session.hash).run();
      return reply({ user: null }, 200, { 'Set-Cookie': sessionCookie(url, '', 0) });
    }
  }
  throw new HttpError(404, '找不到这个页面。');
}
