const MAX_BODY = 350_000;
const PAGE_SIZE = 9;
const EXAMPLES = [
  ['leithanien', '莱塔尼亚 · 回响叠层', '卫戍资料站', '围绕莱塔尼亚建立叠层体系，以折光、复奏积累层数，再补上锏和薇薇安娜。', ['锏','薇薇安娜','黑键','折光','复奏','车尔尼']],
  ['aegir', '阿戈尔 · 深海猎群', '卫戍资料站', '以深海猎人为主体。前期先用幽灵鲨、斯卡蒂过渡，中后期加入歌蕾蒂娅和乌尔比安。', ['乌尔比安','歌蕾蒂娅','归溟幽灵鲨','浊心斯卡蒂','斯卡蒂','幽灵鲨']],
  ['kjerag', '谢拉格 · 冻结阵线', '卫戍资料站', '让灵知负责控制，围绕凛御银灰补足输出，角峰和极光承担前排压力。', ['凛御银灰','圣聆初雪','灵知','极光','角峰','耶拉']],
  ['yan', '炎 · 岁序连携', '卫戍资料站', '拜松、矩先过渡，逐步补入黍、余和仇白，留出高费干员的位置。', ['烛煌','黍','余','仇白','矩','拜松']],
  ['kazimierz', '卡西米尔 · 骑士集结', '卫戍资料站', '用野鬃、砾和瑕光建立前期阵线，后续换上耀骑士临光和玛恩纳。', ['耀骑士临光','玛恩纳','焰尾','瑕光','砾','野鬃']],
  ['siracusa', '叙拉古 · 狼群协同', '卫戍资料站', '从拉普兰德和奥斯塔起步，用忍冬衔接，最终围绕荒芜拉普兰德成型。', ['荒芜拉普兰德','贝洛内','斥罪','忍冬','奥斯塔','拉普兰德']]
];

class HttpError extends Error {
  constructor(status, message, code) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

const hasOwn = (object, key) => Object.hasOwn(object, key);
const validId = (value) => typeof value === 'string' && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value);
const cleanText = (value) => typeof value === 'string' ? value.trim() : '';
const lineupOperators = { ...catalog.operators, ...selfSelectedOperators };
const pageNumber = (params) => Math.min(100000, Math.max(1, parseInt(params.get('page'), 10) || 1));
const lineupNotFound = () => new HttpError(404, '找不到这个阵容。');

function parseShare(value) {
  let parsed;
  try {
    parsed = parseLineupShare(value);
    validateLineupCatalog(parsed.content, lineupOperators, catalog.equipment);
  } catch (error) {
    throw new HttpError(400, error.message);
  }
  const { content: data, share } = parsed;
  const factions = new Map();
  const members = data.Members.map((member) => {
    const { OperatorName: name, IsStackingCore: stacking, IsDamageCore: damage } = member;
    const op = lineupOperators[name];
    for (const covenant of op.covenants) factions.set(covenant, (factions.get(covenant) ?? 0) + 1);
    return { name, stacking, damage, equipment: memberEquipmentSlots(member).filter(Boolean), image: op.image, tier: op.tier,
      ...(member.IsElite && (op.tier > 0 || op.selfSelected) ? { elite: true } : {}) };
  });
  const covenants = data.CovenantTags?.length ? data.CovenantTags :
    [...factions].sort((a, b) => b[1] - a[1]).map(pair => pair[0]);
  return {
    title: data.Name, author: data.Author, description: data.Description ?? '', members,
    strategies: data.AvailableStrategies ?? [],
    covenants,
    search: [data.Name, data.Author, ...members.flatMap(member => [member.name, ...member.equipment]), ...covenants, ...factions.keys()]
      .join(' ').normalize('NFKC').toLowerCase(),
    share,
    sample: false
  };
}

const demos = EXAMPLES.map(([id, title, author, description, names], index) => {
  const data = parseShare('ASPA-LINEUP/2\n' + SHARE_COMMENT + '\n' + JSON.stringify({
    Name: title, Author: author, Description: description,
    Members: names.map((name, i) => ({ OperatorName: name, IsDamageCore: i === 0, IsStackingCore: i === 1 }))
  }));
  return { id: 'demo-' + id, created_at: 1_789_000_000_000 - index, data: { ...data, sample: true } };
});
const demoJson = JSON.stringify(demos);
const validTarget = (id) => validId(id) || demos.some(demo => demo.id === id);

function shortToken(id) {
  if (!validId(id)) return id;
  const bytes = id.replaceAll('-', '').match(/../g).map(byte => parseInt(byte, 16));
  return btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}

function idFromShortToken(token) {
  if (demos.some(demo => demo.id === token)) return token;
  if (!/^[A-Za-z0-9_-]{22}$/.test(token)) throw new HttpError(404, '找不到这个阵容短链接。');
  const hex = [...atob(token.replaceAll('-', '+').replaceAll('_', '/') + '==')]
    .map(byte => byte.charCodeAt(0).toString(16).padStart(2, '0')).join('');
  const id = [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-');
  if (shortToken(id) !== token) throw new HttpError(404, '找不到这个阵容短链接。');
  return id;
}

const shortPathFor = id => '/s/' + shortToken(id);
const visible = `WITH visible AS (
  SELECT id, created_at, data, account_id, revision FROM lineups
  UNION ALL
  SELECT json_extract(value,'$.id'), json_extract(value,'$.created_at'), json_extract(value,'$.data'), NULL, NULL FROM json_each(?)
)`;
const summary = `id, created_at, data,
  (SELECT COUNT(*) FROM comments WHERE lineup_id = visible.id) AS comment_count,
  (SELECT COUNT(*) FROM ratings WHERE lineup_id = visible.id) AS rating_count,
  (SELECT AVG(score) FROM ratings WHERE lineup_id = visible.id) AS rating`;

function activeCovenantsFor(members) {
  const counts = new Map();
  let eliteCount = 0;
  for (const member of members) {
    if (!hasOwn(lineupOperators, member.name)) continue;
    const op = lineupOperators[member.name];
    const memberships = new Set([...op.covenants, ...(op.additionalCovenants ?? [])]);
    if (member.equipment.some(name => catalog.jobChangeActivators.includes(name))) {
      for (const name of member.equipment) {
        if (hasOwn(catalog.jobChangeEquipment, name)) memberships.add(catalog.jobChangeEquipment[name]);
      }
    }
    for (const name of memberships) counts.set(name, (counts.get(name) ?? 0) + 1);
    if (member.elite && (op.tier > 0 || op.selfSelected)) eliteCount++;
  }

  const rules = Object.entries(catalog.activationRules);
  const meets = (rule, count) => count >= rule.minimumCount && (rule.maximumCount == null || count <= rule.maximumCount);
  const coreBonus = rules.reduce((bonus, [name, rule]) =>
    meets(rule, counts.get(name) ?? 0) ? bonus + (rule.coreActivationBonus ?? 0) : bonus, 0);
  return rules.filter(([name, rule]) => {
    if (rule.requiresElite) return meets(rule, eliteCount);
    const count = counts.get(name) ?? 0;
    return meets(rule, count > 0 && hasOwn(catalog.covenants, name) ? count + coreBonus : count);
  }).map(([name]) => name);
}

function lineupFromRow(row, detail = false) {
  const { title, author, description, covenants, members, strategies = [], sample, share } = JSON.parse(row.data);
  const { originalAuthor, originalVideoUrl } = splitLineupDescription(description);
  const sourceUrl = lineupSourceHref(originalVideoUrl);
  const visibleStrategies = detail ? strategies : strategies.slice(0, 5);
  const strategyPortraits = visibleStrategies.map(name => ({
    name,
    image: Object.hasOwn(catalog.strategies, name) ? catalog.strategies[name].image : null
  }));
  return {
    id: row.id, createdAt: row.created_at, title, author,
    description: detail ? description : description.slice(0, 160),
    ...(sourceUrl ? { attribution: { author: originalAuthor, url: sourceUrl } } : {}),
    covenants, activeCovenants: activeCovenantsFor(members), members, strategyPortraits,
    strategyCount: strategies.length, sample, commentCount: row.comment_count,
    ratingCount: row.rating_count, rating: row.rating === null ? null : Math.round(row.rating * 10) / 10,
    shortPath: shortPathFor(row.id), ...(detail ? { strategies, share } : {})
  };
}

async function listLineups(db, params) {
  const q = (params.get('q') ?? '').slice(0, 100).normalize('NFKC').trim().toLowerCase();
  const covenant = params.get('covenant') ?? '';
  const source = params.get('source') ?? '';
  const page = pageNumber(params);
  const minimum = params.get('rated') === '1' ? 1 : 0;
  const filters = [
    "instr(json_extract(data,'$.search'), ?) > 0",
    "(? = '' OR EXISTS (SELECT 1 FROM json_each(data,'$.covenants') WHERE value = ?))",
    "(? = '' OR json_extract(data,'$.sample') = ?)",
    "(? = 0 OR EXISTS (SELECT 1 FROM ratings WHERE lineup_id = visible.id))"
  ].join(' AND ');
  const values = [demoJson, q, covenant, covenant, source, source === 'sample' ? 1 : 0, minimum];
  const sort = params.get('sort');
  const order = sort === 'rating' ? 'rating DESC, rating_count DESC, created_at DESC, id'
    : sort === 'comments' ? 'comment_count DESC, created_at DESC, id' : 'created_at DESC, id';
  const { count } = await db.prepare(visible + ' SELECT COUNT(*) AS count FROM visible WHERE ' + filters).bind(...values).first();
  const { results } = await db.prepare(visible + ' SELECT ' + summary + ' FROM visible WHERE ' + filters
    + ' ORDER BY ' + order + ' LIMIT ? OFFSET ?').bind(...values, PAGE_SIZE, (page - 1) * PAGE_SIZE).all();
  return { items: results.map(row => lineupFromRow(row)), total: count, page, pageSize: PAGE_SIZE };
}

async function listAccountLineups(db, accountId, params) {
  const view = params.get('view') ?? 'published';
  if (view !== 'published' && view !== 'favorites') throw new HttpError(400, '个人阵容列表类型无效。');
  const page = pageNumber(params);
  const favorites = view === 'favorites';
  const scope = favorites
    ? 'SELECT visible.*, favorite.created_at AS order_at FROM visible'
      + ' JOIN lineup_favorites AS favorite ON favorite.lineup_id = visible.id WHERE favorite.account_id = ?'
    : 'SELECT *, created_at AS order_at FROM visible WHERE account_id = ?';
  const personal = visible + ', personal AS (' + scope + ')';
  const { count } = await db.prepare(personal + ' SELECT COUNT(*) AS count FROM personal').bind(demoJson, accountId).first();
  const { results } = await db.prepare(personal + ' SELECT ' + summary + ', revision FROM personal AS visible'
    + ' ORDER BY order_at DESC, id LIMIT ? OFFSET ?').bind(demoJson, accountId, PAGE_SIZE, (page - 1) * PAGE_SIZE).all();
  const items = results.map(row => ({ ...lineupFromRow(row), ...(favorites ? { isFavorite: true } : { revision: row.revision }) }));
  return { items, total: count, page, pageSize: PAGE_SIZE };
}

async function findLineup(db, id, accountId = null) {
  if (!validTarget(id)) throw lineupNotFound();
  const row = await db.prepare(visible + ' SELECT ' + summary
    + ', CASE WHEN account_id = ? THEN revision END AS owned_revision FROM visible WHERE id = ?')
    .bind(demoJson, accountId, id).first();
  if (!row) throw lineupNotFound();
  return { lineup: lineupFromRow(row, true), revision: row.owned_revision ?? undefined };
}

async function setFavorite(db, id, accountId, favorite) {
  if (typeof favorite !== 'boolean') throw new HttpError(400, '收藏状态无效。');
  if (favorite) {
    const row = await db.prepare(visible + ' INSERT INTO lineup_favorites (account_id, lineup_id, created_at)'
      + ' SELECT ?, id, ? FROM visible WHERE id = ?'
      + ' ON CONFLICT(account_id, lineup_id) DO UPDATE SET lineup_id = excluded.lineup_id RETURNING lineup_id')
      .bind(demoJson, accountId, Date.now(), id).first();
    if (!row) throw lineupNotFound();
  } else {
    await findLineup(db, id);
    await db.prepare('DELETE FROM lineup_favorites WHERE lineup_id = ? AND account_id = ?').bind(id, accountId).run();
  }
  return { favorite };
}

async function deleteLineup(db, id, accountId, revision) {
  if (!Number.isSafeInteger(revision) || revision < 0) throw new HttpError(400, '阵容版本无效。');
  const deleted = await db.prepare('DELETE FROM lineups WHERE id = ? AND account_id = ? AND revision = ? RETURNING id')
    .bind(id, accountId, revision).first();
  if (deleted) return deleted;
  const current = await findLineup(db, id, accountId);
  if (current.revision === undefined) throw new HttpError(403, '只有发布账号可以删除此阵容。');
  throw new HttpError(409, '此阵容已有新版本。请重新打开阵容后再删除。', 'revision_conflict');
}

async function listNotifications(db, accountId, params) {
  const page = pageNumber(params);
  const { total, unread } = await db.prepare('SELECT COUNT(*) AS total, COUNT(*) - COUNT(read_at) AS unread'
    + ' FROM lineup_comment_notifications WHERE recipient_id = ?').bind(accountId).first();
  const { results } = await db.prepare(`SELECT notification.id, notification.lineup_id AS lineupId,
    json_extract(lineups.data, '$.title') AS title, comments.text AS comment, notification.created_at AS createdAt,
    notification.read_at AS readAt
    FROM lineup_comment_notifications AS notification
    JOIN lineups ON lineups.id = notification.lineup_id
    JOIN comments ON comments.id = notification.id
    WHERE notification.recipient_id = ?
    ORDER BY notification.created_at DESC, notification.id LIMIT ? OFFSET ?`).bind(accountId, PAGE_SIZE, (page - 1) * PAGE_SIZE).all();
  const items = results.map(({ readAt, ...item }) => ({ ...item, read: readAt !== null }));
  return { items, total, unread, page, pageSize: PAGE_SIZE };
}

async function markNotificationRead(db, id, accountId) {
  if (!validId(id)) throw new HttpError(404, '找不到这条通知。');
  const result = await db.prepare('UPDATE lineup_comment_notifications SET read_at = COALESCE(read_at, ?)'
    + ' WHERE id = ? AND recipient_id = ? RETURNING id').bind(Date.now(), id, accountId).first();
  if (!result) throw new HttpError(404, '找不到这条通知。');
  const { unread } = await db.prepare('SELECT COUNT(*) AS unread FROM lineup_comment_notifications'
    + ' WHERE recipient_id = ? AND read_at IS NULL').bind(accountId).first();
  return { id, unread };
}

async function addComment(db, lineupId, commenterId, text) {
  const id = crypto.randomUUID();
  const insert = db.prepare(visible + ' INSERT INTO comments (id, lineup_id, text, created_at)'
    + ' SELECT ?, id, ?, ? FROM visible WHERE id = ?').bind(demoJson, id, text, Date.now(), lineupId);
  const notify = db.prepare(`INSERT INTO lineup_comment_notifications (id, lineup_id, recipient_id, created_at)
    SELECT comments.id, lineups.id, lineups.account_id, comments.created_at
    FROM comments JOIN lineups ON lineups.id = comments.lineup_id
    WHERE comments.id = ? AND lineups.account_id <> ?`).bind(id, commenterId);
  const [inserted] = await db.batch([insert, notify]);
  if (inserted.changes !== 1) throw lineupNotFound();
}

async function ownedRevision(db, id, accountId) {
  const row = await db.prepare('SELECT revision FROM lineups WHERE id = ? AND account_id = ?')
    .bind(id, accountId).first();
  return row?.revision;
}

function visitorFrom(request) {
  const candidate = cookieValue(request, 'asp_visitor');
  return validId(candidate) ? candidate : null;
}

function reply(body, status = 200, headers = {}) {
  return Response.json(body, { status, headers: { 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff', ...headers } });
}

async function readBody(request, limit = MAX_BODY) {
  const requestOrigin = request.headers.get('origin');
  if (requestOrigin && requestOrigin !== new URL(request.url).origin) throw new HttpError(403, '请求来源无效。');
  if (!/^application\/json(?:\s*;|$)/i.test(request.headers.get('content-type') ?? '') || !request.body) {
    throw new HttpError(415, '请使用 JSON 提交。');
  }
  const reader = request.body.getReader();
  const decoder = new TextDecoder('utf-8', { fatal: true });
  let json = '', size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > limit) {
        await reader.cancel();
        throw new HttpError(413, '提交的内容过长。');
      }
      json += decoder.decode(value, { stream: true });
    }
    const body = JSON.parse(json + decoder.decode());
    if (!body || typeof body !== 'object' || Array.isArray(body)) throw new Error('Expected object');
    return body;
  } catch (error) {
    if (error instanceof HttpError) throw error;
    throw new HttpError(400, '无法读取提交内容。');
  }
}

async function publishFromDesktop(request, db) {
  const user = await requireSession(db, request, true);
  const { share, localId } = await readBody(request);
  if (!validId(localId)) throw new HttpError(400, '阵容标识无效。');
  const data = parseShare(share);
  // Retrying the same local snapshot returns its original publication.
  const hash = await digest(JSON.stringify([user.id, localId, data.share]));
  const id = [hash.slice(0, 8), hash.slice(8, 12), hash.slice(12, 16), hash.slice(16, 20), hash.slice(20, 32)].join('-');
  const existing = await db.prepare('SELECT id FROM lineups WHERE id = ?').bind(id).first();
  if (existing) return reply({ id });

  const now = Date.now();
  await takeAuthLimit(db, 'desktop-publish:' + user.id, now + 60 * 60_000, 20, now, '阵容上传过于频繁，请稍后再试。');
  const inserted = await db.prepare('INSERT INTO lineups (id, created_at, data, account_id) VALUES (?, ?, ?, ?)'
    + ' ON CONFLICT(id) DO NOTHING RETURNING id').bind(id, now, JSON.stringify(data), user.id).first();
  return reply({ id }, inserted ? 201 : 200);
}

async function api(request, url, env) {
  const { DB: db } = env;
  const path = url.pathname;
  if (path.startsWith('/api/auth/')) return authApi(request, url, env);
  if (path === '/api/operator-votes' || path === '/api/operator-votes/mine')
    return operatorVotesApi(request, url, env);
  const method = request.method;
  if (method === 'POST' && path === '/api/desktop/feedback') return submitFeedback(request, env);
  if (method === 'POST' && path === '/api/share') return createShareSnapshot(request, env);
  const short = /^\/api\/share\/([A-Za-z0-9_-]{1,32})$/.exec(path);
  if (method === 'GET' && short) {
    const token = short[1];
    if (SNAPSHOT_TOKEN.test(token)) {
      const data = await readShareSnapshot(env, token);
      const shortPath = '/s/' + token;
      if (url.searchParams.get('preview') !== '1') return reply({ share: data.share, shortPath });
      const lineup = lineupFromRow({ id: token, data: JSON.stringify(data), rating: null }, true);
      return reply({ lineup: { ...lineup, shortPath } });
    }
    const { lineup } = await findLineup(db, idFromShortToken(short[1]));
    return reply({ share: lineup.share, shortPath: lineup.shortPath });
  }
  if (method === 'GET' && path === '/api/catalog') return reply({ covenants: Object.fromEntries(
    catalog.covenantTags.map(name => [name, catalog.covenants[name] ?? null])) });
  if (method === 'GET' && path === '/api/editor-catalog') return reply(catalog);
  if (method === 'GET' && path === '/api/self-selected-operators') return reply(selfSelectedOperators);
  if (method === 'GET' && path === '/api/lineups') return reply(await listLineups(db, url.searchParams));
  if (method === 'GET' && path === '/api/me/lineups') {
    const user = await requireSession(db, request);
    return reply(await listAccountLineups(db, user.id, url.searchParams));
  }
  if (method === 'GET' && path === '/api/me/notifications') {
    const user = await requireSession(db, request);
    return reply(await listNotifications(db, user.id, url.searchParams));
  }
  const notice = /^\/api\/me\/notifications\/([a-z0-9-]+)\/read$/.exec(path);
  if (method === 'PUT' && notice) {
    const user = await requireSession(db, request);
    await readBody(request, 1024);
    return reply(await markNotificationRead(db, notice[1], user.id));
  }
  if (method === 'POST' && path === '/api/captcha') {
    const { action, target = '' } = await readBody(request, 2048);
    if (['publish', 'login'].includes(action) ? target !== '' : !['comment', 'rate', 'edit'].includes(action) || !validTarget(target)) {
      throw new HttpError(400, '验证码用途无效。');
    }
    if (action !== 'login') {
      const user = await requireSession(db, request);
      if (target) await findLineup(db, target);
      if (action === 'edit' && await ownedRevision(db, target, user.id) === undefined) {
        throw new HttpError(403, '只有发布账号可以编辑此阵容。');
      }
      return reply(await issueCaptcha(db, env.clientIp, user.hash, action, target), 201);
    }
    const visitor = visitorFrom(request);
    const key = visitor ?? crypto.randomUUID();
    const secure = url.protocol === 'https:' ? '; Secure' : '';
    return reply(await issueCaptcha(db, env.clientIp, key, action, target), 201,
      visitor ? {} : { 'Set-Cookie': 'asp_visitor=' + key + '; Path=/; Max-Age=86400; HttpOnly; SameSite=Lax' + secure });
  }
  if (method === 'POST' && path === '/api/desktop/lineups') return publishFromDesktop(request, db);
  if (method === 'POST' && path === '/api/lineups') {
    const user = await requireSession(db, request);
    const body = await readBody(request);
    const data = parseShare(body.share);
    await verifyCaptcha(db, user.hash, 'publish', '', body.captcha);
    const id = crypto.randomUUID();
    await db.prepare('INSERT INTO lineups (id, created_at, data, account_id) VALUES (?, ?, ?, ?)')
      .bind(id, Date.now(), JSON.stringify(data), user.id).run();
    return reply({ id, shortPath: shortPathFor(id) }, 201);
  }
  const route = /^\/api\/lineups\/([a-z0-9-]+)(?:\/(comments|rating|favorite))?$/.exec(path);
  if (route) {
    const [, id, action] = route;
    if (method === 'GET' && !action) {
      const user = await readSession(db, request);
      const { lineup, revision } = await findLineup(db, id, user?.id);
      const page = Math.min(10000, Math.max(1, parseInt(url.searchParams.get('commentsPage'), 10) || 1));
      const { results } = await db.prepare(
        'SELECT id, text, created_at FROM comments WHERE lineup_id = ? ORDER BY created_at DESC, id LIMIT 25 OFFSET ?'
      ).bind(id, (page - 1) * 25).all();
      const mine = user && await db.prepare('SELECT score FROM ratings WHERE lineup_id = ? AND voter = ?')
        .bind(id, 'account:' + user.id).first();
      const isFavorite = Boolean(user && await db.prepare('SELECT 1 FROM lineup_favorites WHERE lineup_id = ? AND account_id = ?')
        .bind(id, user.id).first());
      return reply({ lineup, comments: results, myScore: mine?.score ?? null, commentsPage: page,
        canEdit: revision !== undefined, revision, isFavorite });
    }
    if (method === 'PUT' && action === 'favorite') {
      const user = await requireSession(db, request);
      const { favorite } = await readBody(request, 1024);
      return reply(await setFavorite(db, id, user.id, favorite));
    }
    if (method === 'DELETE' && !action) {
      const user = await requireSession(db, request);
      const { revision } = await readBody(request, 1024);
      return reply(await deleteLineup(db, id, user.id, revision));
    }
    if (method === 'PATCH' && !action) {
      const user = await requireSession(db, request);
      await findLineup(db, id);
      const revision = await ownedRevision(db, id, user.id);
      if (revision === undefined) throw new HttpError(403, '只有发布账号可以编辑此阵容。');
      const body = await readBody(request);
      const conflict = () => new HttpError(409, '此阵容已有新版本。请复制当前草稿，再重新打开阵容编辑。', 'revision_conflict');
      if (body.revision !== revision) throw conflict();
      const data = parseShare(body.share);
      await verifyCaptcha(db, user.hash, 'edit', id, body.captcha);
      const update = await db.prepare('UPDATE lineups SET data = ?, revision = revision + 1 WHERE id = ? AND account_id = ? AND revision = ?')
        .bind(JSON.stringify(data), id, user.id, revision).run();
      if (update.changes !== 1) throw conflict();
      return reply({ id });
    }
    if (method === 'POST' && (action === 'comments' || action === 'rating')) {
      const user = await requireSession(db, request);
      await findLineup(db, id);
      const body = await readBody(request, 8192);
      if (action === 'comments') {
        const text = cleanText(body.text);
        if (!text || text.length > 600) throw new HttpError(400, '评论需在 1–600 字以内。');
        await verifyCaptcha(db, user.hash, 'comment', id, body.captcha);
        await addComment(db, id, user.id, text);
        return reply({ ok: true }, 201);
      }
      const score = body.score;
      if (!Number.isInteger(score) || score < 1 || score > 5) throw new HttpError(400, '请选择 1–5 星。');
      await verifyCaptcha(db, user.hash, 'rate', id, body.captcha);
      const rating = await db.prepare(visible + ' INSERT INTO ratings (lineup_id, voter, score)'
        + ' SELECT id, ?, ? FROM visible WHERE id = ? ON CONFLICT(lineup_id, voter) DO UPDATE SET score = excluded.score')
        .bind(demoJson, 'account:' + user.id, score, id).run();
      if (rating.changes !== 1) throw lineupNotFound();
      return reply({ ok: true });
    }
  }
  throw new HttpError(404, '找不到这个页面。');
}

const contentTypes = { '/': 'text/html; charset=utf-8', '/style.css': 'text/css; charset=utf-8',
  '/app.js': 'text/javascript; charset=utf-8' };
const security = {
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'same-origin',
  'Content-Security-Policy': "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'"
};

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;
    try {
      if (request.method === 'GET' && path === '/healthz') {
        await env.DB.prepare('SELECT 1 FROM accounts LIMIT 1').first();
        return reply({ ok: true });
      }
      if (path.startsWith('/api/')) return await api(request, url, env);
      if (request.method === 'GET' || request.method === 'HEAD') {
        const short = /^\/s\/([A-Za-z0-9_-]{1,32})$/.exec(path);
        if (short) {
          const token = short[1];
          let location;
          if (SNAPSHOT_TOKEN.test(token)) {
            await readShareSnapshot(env, token);
            location = '/?share=' + token;
          } else {
            const id = idFromShortToken(token);
            await findLineup(env.DB, id);
            location = '/?lineup=' + id;
          }
          return new Response(null, { status: 302, headers: { Location: location, 'Cache-Control': 'no-store' } });
        }
        const isHead = request.method === 'HEAD';
        if (hasOwn(pages, path)) return new Response(isHead ? null : pages[path], { headers: {
          ...security, 'Content-Type': contentTypes[path], 'Cache-Control': 'no-cache'
        } });
        const portrait = /^\/portraits\/([a-z0-9]+)\.webp$/.exec(path)?.[1];
        const gear = /^\/gear-icons\/([a-f0-9]{16})\.png$/.exec(path)?.[1];
        const strategy = /^\/strategy-icons\/([a-f0-9]{16})\.png$/.exec(path)?.[1];
        const selfSelectedPortrait = /^\/self-selected-portraits\/([a-f0-9]{16})\.png$/.exec(path)?.[1];
        const asset = portrait && hasOwn(portraits, portrait) ? portraits[portrait]
          : gear ? gearIcons[gear] : strategy ? strategyIcons[strategy]
            : selfSelectedPortrait && selfSelectedPortraits[selfSelectedPortrait];
        if (asset) {
          return new Response(isHead ? null : Uint8Array.from(atob(asset), c => c.charCodeAt(0)), {
            headers: { ...security, 'Content-Type': portrait ? 'image/webp' : 'image/png', 'Cache-Control': 'public, max-age=86400' }
          });
        }
      }
      return reply({ error: '找不到这个页面。' }, 404);
    } catch (error) {
      if (error instanceof HttpError) return reply({ error: error.message, code: error.code }, error.status);
      console.error(error);
      return reply({ error: '服务器暂时无法处理请求，请稍后重试。' }, 500);
    }
  }
};
