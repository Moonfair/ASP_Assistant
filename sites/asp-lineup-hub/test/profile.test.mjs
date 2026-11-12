import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, randomBytes } from 'node:crypto';
import { openDatabase } from '../scripts/local-db.mjs';
import worker from '../dist/server/index.js';

const origin = 'https://lineup.example.test';
const share = 'ASPA-LINEUP/2\n' + JSON.stringify({
  Name: '测试公开阵容', Author: '测试作者', Members: [{ OperatorName: '凛御银灰' }]
});
const hash = value => createHash('sha256').update(value).digest('hex');

function setup(t) {
  const DB = openDatabase(':memory:');
  t.after(() => DB.close());
  const env = { DB, SHARE_SECRET: randomBytes(32), clientIp: '192.0.2.1' };

  async function request(path, { token, method = 'GET', body, from = origin } = {}) {
    const headers = {};
    if (token) headers.cookie = 'asp_session=' + token;
    if (body) Object.assign(headers, { 'Content-Type': 'application/json', Origin: from });
    const response = await worker.fetch(new Request(origin + path, { method, headers, body: body && JSON.stringify(body) }), env);
    return { status: response.status, data: await response.json() };
  }

  async function account(name) {
    const id = crypto.randomUUID();
    const token = randomBytes(32).toString('hex');
    const now = Date.now();
    await DB.prepare('INSERT INTO accounts VALUES (?, ?, ?)').bind(id, name + '@example.test', now).run();
    await DB.prepare('INSERT INTO auth_sessions VALUES (?, ?, ?)').bind(hash(token), id, now + 60_000).run();
    async function captcha(action, target = '') {
      const id = crypto.randomUUID();
      const answer = 'ABCDE';
      await DB.prepare('INSERT INTO challenges VALUES (?, ?, ?, ?, ?, ?)')
        .bind(id, hash(token), action, target, hash(id + ':' + answer), Date.now() + 60_000).run();
      return { id, answer };
    }
    return { request: (path, options) => request(path, { ...options, token }), captcha };
  }

  return { DB, request, account };
}

async function publish(owner) {
  const result = await owner.request('/api/lineups', { method: 'POST', body: { share, captcha: await owner.captcha('publish') } });
  assert.equal(result.status, 201);
  return result.data;
}

const write = (method, body, from) => ({ method, body, from });
const favorite = (value, from) => write('PUT', { favorite: value }, from);
const count = async (db, table, id) => (await db.prepare('SELECT COUNT(*) AS count FROM ' + table + ' WHERE lineup_id = ?')
  .bind(id).first()).count;

test('本人列表分页与收藏跟随当前账号，示例阵容也可收藏', async (t) => {
  const { request, account } = setup(t);
  const owner = await account('owner'), other = await account('other');
  const { id } = await publish(owner);
  const second = await publish(other);
  const detail = '/api/lineups/' + id;
  const favoritePath = detail + '/favorite';
  const demoFavorite = '/api/lineups/demo-yan/favorite';
  assert.equal((await request('/api/me/lineups?view=favorites')).status, 401);
  assert.equal((await request(favoritePath, favorite(true))).status, 401);
  assert.equal((await other.request(favoritePath, favorite(true, 'https://outside.example'))).status, 403);
  assert.equal((await other.request(favoritePath, favorite('yes'))).status, 400);
  assert.deepEqual(await other.request(favoritePath, favorite(true)), { status: 200, data: { favorite: true } });
  assert.deepEqual(await other.request(favoritePath, favorite(true)), { status: 200, data: { favorite: true } });
  assert.deepEqual(await other.request(demoFavorite, favorite(true)), { status: 200, data: { favorite: true } });
  assert.equal((await request(detail)).data.isFavorite, false);
  assert.equal((await owner.request(detail)).data.isFavorite, false);
  assert.equal((await other.request(detail)).data.isFavorite, true);
  assert.equal((await other.request('/api/lineups/demo-yan')).data.isFavorite, true);

  const published = (await owner.request('/api/me/lineups?view=published')).data;
  assert.deepEqual(published.items.map(item => [item.id, item.revision]), [[id, 0]]);
  assert.equal(published.total, 1);
  assert.deepEqual((await other.request('/api/me/lineups?view=published')).data.items.map(item => item.id), [second.id]);
  assert.equal((await owner.request('/api/me/lineups?view=favorites')).data.total, 0);
  const saved = (await other.request('/api/me/lineups?view=favorites')).data;
  assert.equal(saved.total, 2);
  assert.deepEqual(new Set(saved.items.map(item => item.id)), new Set([id, 'demo-yan']));
  assert.ok(saved.items.every(item => item.isFavorite));
  assert.deepEqual((await other.request('/api/me/lineups?view=favorites&page=2')).data,
    { items: [], total: 2, page: 2, pageSize: saved.pageSize });
  assert.deepEqual(await other.request(favoritePath, favorite(false)), { status: 200, data: { favorite: false } });
  assert.deepEqual(await other.request(favoritePath, favorite(false)), { status: 200, data: { favorite: false } });
  assert.equal((await other.request(detail)).data.isFavorite, false);
});

test('删除仅限本人和当前版本，清理互动且独立分享快照继续可用', async (t) => {
  const { DB, request, account } = setup(t);
  const owner = await account('owner'), other = await account('other');
  const { id, shortPath } = await publish(owner);
  const detail = '/api/lineups/' + id;
  const snapshot = await request('/api/share', write('POST', { share }));
  assert.equal(snapshot.status, 201);
  assert.equal((await other.request(detail + '/favorite', favorite(true))).status, 200);
  assert.equal((await other.request(detail + '/comments', write('POST', { text: '匿名评论', captcha: await other.captcha('comment', id) }))).status, 201);
  assert.equal((await other.request(detail + '/rating', write('POST', { score: 4, captcha: await other.captcha('rate', id) }))).status, 200);
  const edit = { share, revision: 0, captcha: await owner.captcha('edit', id) };
  assert.equal((await owner.request(detail, write('PATCH', edit))).status, 200);
  assert.equal((await owner.request('/api/me/lineups?view=published')).data.items[0].revision, 1);
  assert.equal((await request(detail, write('DELETE', { revision: 1 }))).status, 401);
  assert.equal((await other.request(detail, write('DELETE', { revision: 1 }))).status, 403);
  assert.equal((await owner.request(detail, write('DELETE', { revision: 1 }, 'https://outside.example'))).status, 403);
  const stale = await owner.request(detail, write('DELETE', { revision: 0 }));
  assert.equal(stale.status, 409);
  assert.equal(stale.data.code, 'revision_conflict');
  assert.equal((await owner.request('/api/lineups/demo-yan', write('DELETE', { revision: 0 }))).status, 403);

  const [removed, racingFavorite] = await Promise.all([
    owner.request(detail, write('DELETE', { revision: 1 })),
    other.request(detail + '/favorite', favorite(true))
  ]);
  assert.deepEqual(removed, { status: 200, data: { id } });
  assert.ok([200, 404].includes(racingFavorite.status));
  assert.equal((await other.request(detail + '/favorite', favorite(true))).status, 404);
  assert.equal((await request(detail)).status, 404);
  for (const table of ['lineup_favorites', 'comments', 'ratings', 'lineup_comment_notifications']) {
    assert.equal(await count(DB, table, id), 0, table);
  }
  assert.equal((await other.request('/api/me/lineups?view=favorites')).data.total, 0);
  assert.equal((await owner.request('/api/me/lineups?view=published')).data.total, 0);
  assert.equal((await request(shortPath.replace('/s/', '/api/share/'))).status, 404);
  assert.equal((await request(snapshot.data.shortPath.replace('/s/', '/api/share/'))).data.share, share);
});

test('匿名评论只通知发布人，已读操作隔离账号且可重试', async (t) => {
  const { DB, request, account } = setup(t);
  const owner = await account('owner'), other = await account('other');
  const { id } = await publish(owner);
  const detail = '/api/lineups/' + id;
  const path = '/api/me/notifications';
  assert.equal((await request(path)).status, 401);
  assert.equal((await owner.request(detail + '/comments', write('POST', { text: '自己的评论', captcha: await owner.captcha('comment', id) }))).status, 201);
  assert.equal((await owner.request(path)).data.total, 0);
  const comment = '这是另一位玩家的评论';
  assert.equal((await other.request(detail + '/comments', write('POST', { text: comment, captcha: await other.captcha('comment', id) }))).status, 201);
  const { items, total, unread, pageSize } = (await owner.request(path)).data;
  const [notice] = items;
  assert.deepEqual({ total, unread, pageSize }, { total: 1, unread: 1, pageSize: 9 });
  assert.deepEqual(notice, { id: notice.id, lineupId: id, title: '测试公开阵容', comment,
    createdAt: notice.createdAt, read: false });
  assert.ok(Number.isSafeInteger(notice.createdAt));
  assert.deepEqual((await owner.request(path + '?page=2')).data, { items: [], total: 1, unread: 1, page: 2, pageSize });
  assert.equal((await other.request(path)).data.total, 0);
  const publicComment = (await request(detail)).data.comments.find(item => item.text === comment);
  assert.deepEqual(Object.keys(publicComment).sort(), ['created_at', 'id', 'text']);

  const read = path + '/' + notice.id + '/read';
  assert.equal((await request(read, write('PUT', {}))).status, 401);
  assert.equal((await other.request(read, write('PUT', {}))).status, 404);
  assert.equal((await owner.request(read, write('PUT', {}, 'https://outside.example'))).status, 403);
  assert.deepEqual(await owner.request(read, write('PUT', {})), { status: 200, data: { id: notice.id, unread: 0 } });
  assert.deepEqual(await owner.request(read, write('PUT', {})), { status: 200, data: { id: notice.id, unread: 0 } });
  assert.equal((await owner.request(path)).data.items[0].read, true);

  await DB.prepare(`CREATE TRIGGER reject_notification BEFORE INSERT ON lineup_comment_notifications BEGIN
    SELECT RAISE(ABORT, 'Test notification failure'); END`).run();
  const expectedError = t.mock.method(console, 'error', () => {});
  const failure = await other.request(detail + '/comments', write('POST', { text: '必须一起回滚', captcha: await other.captcha('comment', id) }));
  expectedError.mock.restore();
  assert.equal(failure.status, 500);
  assert.equal(await count(DB, 'comments', id), 2);
  assert.equal((await owner.request(path)).data.total, 1);
});
