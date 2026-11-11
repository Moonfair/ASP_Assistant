const SNAPSHOT_TOKEN = /^u-[A-Za-z0-9_-]{22}$/;
const SNAPSHOT_TTL = 90 * 24 * 60 * 60_000;
const SNAPSHOT_WINDOW = 60 * 60_000;
const snapshotEncoder = new TextEncoder();
const snapshotDecoder = new TextDecoder('utf-8', { fatal: true });

async function snapshotKey(secret) {
  if (!secret || secret.byteLength < 32) throw new HttpError(503, '阵容短链接服务暂不可用，请稍后重试。');
  const key = await crypto.subtle.importKey('raw', secret, 'HKDF', false, ['deriveKey']);
  return crypto.subtle.deriveKey({ name: 'HKDF', hash: 'SHA-256', salt: new Uint8Array(),
    info: snapshotEncoder.encode('asp-lineup-hub/share-snapshot/v1') }, key,
  { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
}

const snapshotCipher = (iv, token) => ({ name: 'AES-GCM', iv, additionalData: snapshotEncoder.encode(token) });

async function createShareSnapshot(request, env) {
  const { DB: db, SHARE_SECRET: secret, clientIp } = env;
  const { share } = await readBody(request);
  const data = parseShare(share);
  const key = await snapshotKey(secret);
  const now = Date.now();
  const limitMessage = '创建阵容短链接过于频繁，请稍后重试。';
  const ipKey = await hmac(secret, 'share-ip:' + (clientIp || 'unknown'));
  await takeAuthLimit(db, 'share-ip:' + ipKey, now + SNAPSHOT_WINDOW, 30, now, limitMessage);
  await takeAuthLimit(db, 'share-global', now + SNAPSHOT_WINDOW, 120, now, limitMessage);
  await db.batch([
    db.prepare('DELETE FROM auth_limits WHERE expires_at <= ?').bind(now),
    db.prepare('DELETE FROM share_snapshots WHERE expires_at <= ?').bind(now)
  ]);

  const token = 'u-' + Buffer.from(crypto.getRandomValues(new Uint8Array(16))).toString('base64url');
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const sealed = await crypto.subtle.encrypt(snapshotCipher(iv, token), key, snapshotEncoder.encode(JSON.stringify(data)));
  const payload = Buffer.concat([iv, new Uint8Array(sealed)]);
  await db.prepare('INSERT INTO share_snapshots (token, expires_at, payload) VALUES (?, ?, ?)')
    .bind(token, now + SNAPSHOT_TTL, payload).run();
  return reply({ shortPath: '/s/' + token }, 201);
}

async function readShareSnapshot(env, token) {
  const missing = () => new HttpError(404, '这个阵容短链接不存在或已过期。');
  const row = await env.DB.prepare('SELECT payload FROM share_snapshots WHERE token = ? AND expires_at > ?')
    .bind(token, Date.now()).first();
  if (!row) throw missing();
  const key = await snapshotKey(env.SHARE_SECRET);
  try {
    const plain = await crypto.subtle.decrypt(snapshotCipher(row.payload.subarray(0, 12), token), key, row.payload.subarray(12));
    return JSON.parse(snapshotDecoder.decode(plain));
  } catch {
    throw missing();
  }
}
