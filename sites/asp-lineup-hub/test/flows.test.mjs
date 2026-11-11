import test from 'node:test';
import assert from 'node:assert/strict';
import { openDatabase } from '../scripts/local-db.mjs';
import worker from '../dist/server/index.js';

const share = 'ASPA-LINEUP/2\n' + JSON.stringify({
  Name: '测试银灰阵容', Author: '测试玩家', Description: '这里是攻略',
  Members: [{ OperatorName: '凛御银灰', IsDamageCore: true, Equipment1: '', Equipment2: '' }]
});

function setup() {
  const DB = openDatabase(':memory:');
  const delivered = [];
  const AUTH = { secret: crypto.getRandomValues(new Uint8Array(32)),
    sendCode: async (email, code) => { delivered.push({ email, code }); } };
  function browser() {
    const cookies = new Map();
    async function request(path, body, origin = 'https://lineup.example.test', method = body ? 'POST' : 'GET', token) {
      const headers = new Headers();
      if (cookies.size) headers.set('Cookie', [...cookies].map(pair => pair.join('=')).join('; '));
      if (body) {
        headers.set('Content-Type', 'application/json');
        if (origin) headers.set('Origin', origin);
      }
      if (token) headers.set('Authorization', 'Bearer ' + token);
      const response = await worker.fetch(new Request('https://lineup.example.test' + path, {
        headers, method, body: body && JSON.stringify(body)
      }), { DB, AUTH, clientIp: '192.0.2.1' });
      const cookie = response.headers.get('Set-Cookie');
      if (cookie) {
        const [name, value] = cookie.split(';')[0].split('=');
        if (value) cookies.set(name, value);
        else cookies.delete(name);
      }
      return { status: response.status, data: await response.json(), cookie };
    }
    async function challenge(action, target = '') {
      const result = await request('/api/captcha', { action, target });
      assert.equal(result.status, 201);
      assert.match(result.data.image, /^data:image\/png;base64,/);
      assert.equal(result.data.answer, undefined);
      const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(result.data.id + ':ABCDE'));
      const digest = Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join('');
      await DB.prepare('UPDATE challenges SET answer_hash = ? WHERE id = ?').bind(digest, result.data.id).run();
      return { id: result.data.id, answer: 'abcde' };
    }
    async function send(email) {
      return request('/api/auth/code', { email, captcha: await challenge('login') });
    }
    async function login(email) {
      const sent = await send(email);
      assert.equal(sent.status, 202);
      return request('/api/auth/verify', { id: sent.data.id, code: delivered.at(-1).code });
    }
    return { request, challenge, send, login };
  }
  return { DB, delivered, browser };
}

test('邮箱首次验证注册，重复登录用同一账号，验证码和退出会话都不可重用', async () => {
  const { DB, delivered, browser } = setup();
  const a = browser(), b = browser();
  assert.equal((await a.request('/api/auth/code', { email: 'a@example.test' }, 'https://other.test')).status, 403);
  assert.equal((await a.request('/api/auth/code', { email: 'a@example.test' })).status, 400);
  const sent = await a.send(' Player@Example.test ');
  assert.equal(sent.status, 202);
  assert.equal(sent.data.code, undefined);
  assert.match(delivered[0].code, /^\d{6}$/);
  assert.equal(delivered[0].email, 'player@example.test');
  const verify = { id: sent.data.id, code: delivered[0].code };
  assert.equal((await b.request('/api/auth/verify', verify)).status, 400);
  const wrong = (Number(verify.code) + 1).toString().padStart(6, '0').slice(-6);
  assert.equal((await a.request('/api/auth/verify', { ...verify, code: wrong })).status, 400);
  const accepted = await Promise.all([a.request('/api/auth/verify', verify), a.request('/api/auth/verify', verify)]);
  assert.deepEqual(accepted.map(response => response.status).sort(), [200, 400]);
  assert.match(accepted.find(response => response.cookie).cookie, /HttpOnly.*SameSite=Lax.*Secure/);
  assert.deepEqual((await a.request('/api/auth/session')).data.user, { email: 'player@example.test' });
  assert.equal((await a.request('/api/auth/logout', {})).status, 200);
  assert.equal((await a.request('/api/auth/session')).data.user, null);
  assert.equal((await DB.prepare('SELECT COUNT(*) AS n FROM auth_sessions').first()).n, 0);
  assert.equal((await b.send('player@example.test')).status, 429);
  await DB.prepare('DELETE FROM auth_limits').run();
  assert.equal((await b.login('player@example.test')).status, 200);
  assert.equal((await DB.prepare('SELECT COUNT(*) AS n FROM accounts').first()).n, 1);
});

test('过期验证码和超过尝试次数的验证码失效', async () => {
  const { DB, delivered, browser } = setup();
  const a = browser();
  const expired = await a.send('expired@example.test');
  await DB.prepare('UPDATE auth_email_codes SET expires_at = 1 WHERE id = ?').bind(expired.data.id).run();
  assert.equal((await a.request('/api/auth/verify', { id: expired.data.id, code: delivered.at(-1).code })).status, 400);
  const locked = await a.send('locked@example.test');
  const code = delivered.at(-1).code;
  const wrong = (Number(code) + 1).toString().padStart(6, '0').slice(-6);
  for (let i = 0; i < 5; i++) {
    assert.equal((await a.request('/api/auth/verify', { id: locked.data.id, code: wrong })).status, 400);
  }
  assert.equal((await a.request('/api/auth/verify', { id: locked.data.id, code })).status, 400);
  assert.equal((await DB.prepare('SELECT COUNT(*) AS n FROM accounts').first()).n, 0);
});

test('登录后发布阵容、匿名评论，换浏览器也只保留一个账号评分', async () => {
  const { DB, browser } = setup();
  const a = browser(), b = browser();
  assert.equal((await a.request('/api/lineups')).status, 200);
  assert.equal((await a.request('/api/lineups', { share })).status, 401);
  assert.equal((await a.login('player@example.test')).status, 200);
  const upload = await a.challenge('publish');
  const posted = await a.request('/api/lineups', { share, captcha: upload });
  assert.equal(posted.status, 201);
  assert.equal((await a.request('/api/lineups', { share, captcha: upload })).status, 400);
  assert.equal((await a.request('/api/lineups?q=银灰&covenant=谢拉格&source=community')).data.total, 1);
  assert.equal((await a.request('/api/lineups?q=银灰&covenant=阿戈尔')).data.total, 0);
  const id = posted.data.id;
  const path = '/api/lineups/' + id;
  assert.equal((await b.request(path + '/comments', { text: '不错！' })).status, 401);
  const comment = await a.challenge('comment', id);
  assert.equal((await a.request(path + '/comments', { text: '不错！', captcha: { ...comment, answer: 'wrong' } })).status, 400);
  assert.equal((await a.request(path + '/comments', { text: '不错！', captcha: comment })).status, 400);
  assert.equal((await a.request(path + '/comments', { text: '不错！', captcha: await a.challenge('comment', id) })).status, 201);
  assert.equal((await a.request(path + '/rating', { score: 4, captcha: await a.challenge('rate', id) })).status, 200);
  await DB.prepare('DELETE FROM auth_limits').run();
  assert.equal((await b.login('player@example.test')).status, 200);
  assert.equal((await b.request(path + '/rating', { score: 5, captcha: await b.challenge('rate', id) })).status, 200);
  const detail = (await a.request(path)).data;
  assert.equal(detail.lineup.ratingCount, 1);
  assert.equal(detail.lineup.rating, 5);
  assert.equal(detail.myScore, 5);
  assert.equal((await browser().request(path)).data.myScore, null);
  assert.deepEqual(Object.keys(detail.comments[0]).sort(), ['created_at', 'id', 'text']);
  assert.equal(detail.comments[0].text, '不错！');
  assert.equal(detail.lineup.share, share);
  assert.equal(JSON.stringify(detail).includes('player@example.test'), false);
});

test('网站可发布带软件介绍的阵容，并在规范化分享文本时保留注释', async () => {
  const { browser } = setup();
  const player = browser();
  assert.equal((await player.login('player@example.test')).status, 200);
  const comment = '# ASP Assistant · 明日方舟「卫戍协议」实时辅助工具 · 官网 https://aspa.moonfair.cn';
  const input = share.replace('\n', '\r\n' + comment + '\r\n').replace('"Name":', '"Extra":true,"Name":');
  const posted = await player.request('/api/lineups', { share: input, captcha: await player.challenge('publish') });
  assert.equal(posted.status, 201);
  const { lineup } = (await player.request('/api/lineups/' + posted.data.id)).data;
  assert.equal(lineup.share.split(/\r?\n/)[1], comment);
  assert.equal(JSON.parse(lineup.share.split(/\r?\n/)[2]).Extra, undefined);
});

test('只有作者账号能更新阵容，保留链接和互动，旧版本不能覆盖新内容', async () => {
  const { browser } = setup();
  const owner = browser(), other = browser();
  await owner.login('owner@example.test');
  await other.login('other@example.test');
  const { data: { id } } = await owner.request('/api/lineups', { share, captcha: await owner.challenge('publish') });
  const path = '/api/lineups/' + id;
  const update = { share: share.replace('测试银灰阵容', '新的银灰阵容'), revision: 0 };
  assert.equal((await owner.request(path)).data.canEdit, true);
  assert.equal((await other.request(path)).data.canEdit, false);
  assert.equal((await other.request('/api/captcha', { action: 'edit', target: id })).status, 403);
  assert.equal((await other.request(path, update, undefined, 'PATCH')).status, 403);
  assert.equal((await other.request(path + '/comments', { text: '喜欢', captcha: await other.challenge('comment', id) })).status, 201);
  assert.equal((await other.request(path + '/rating', { score: 4, captcha: await other.challenge('rate', id) })).status, 200);
  assert.equal((await owner.request(path, update, undefined, 'PATCH')).status, 400);
  assert.equal((await owner.request(path, { ...update, captcha: await owner.challenge('edit', id) }, undefined, 'PATCH')).status, 200);
  assert.equal((await owner.request(path, { ...update, captcha: await owner.challenge('edit', id) }, undefined, 'PATCH')).status, 409);
  const detail = (await other.request(path)).data;
  assert.equal(detail.lineup.id, id);
  assert.equal(detail.lineup.title, '新的银灰阵容');
  assert.equal(detail.lineup.rating, 4);
  assert.equal(detail.comments[0].text, '喜欢');
  assert.equal(detail.revision, undefined);
});

test('发布校验使用侧边栏目录，保留装备槽位置、两个核心标记和图示源码', async () => {
  const { browser } = setup();
  const player = browser();
  await player.login('player@example.test');
  const content = {
    Name: '双核心', Author: '测试', Description: '说明\n\n```lineup\n干员 凛御银灰\n```',
    Members: [{ OperatorName: '凛御银灰', IsStackingCore: true, IsDamageCore: true,
      Equipment1: '', Equipment2: '坚守盾牌' }]
  };
  const encode = () => 'ASPA-LINEUP/2\n' + JSON.stringify(content);
  const captcha = await player.challenge('publish');
  content.Members[0].Equipment2 = '目录中没有的装备';
  assert.equal((await player.request('/api/lineups', { share: encode(), captcha })).status, 400);
  content.Members[0].Equipment2 = '坚守盾牌';
  const result = await player.request('/api/lineups', { share: encode(), captcha });
  assert.equal(result.status, 201);
  const { lineup } = (await player.request('/api/lineups/' + result.data.id)).data;
  assert.deepEqual(JSON.parse(lineup.share.split('\n')[1]), content);
});

test('客户端邮箱登录后可一键发布，重复上传不会创建副本，退出后凭据失效', async () => {
  const { DB, delivered, browser } = setup();
  const desktop = browser();
  const sent = await desktop.send('desktop@example.test');
  assert.equal(sent.status, 202);
  const code = { id: sent.data.id, code: delivered.at(-1).code };
  assert.equal((await desktop.request('/api/auth/desktop/verify', code)).status, 403);
  const login = await desktop.request('/api/auth/desktop/verify', code, null);
  assert.equal(login.status, 200);
  assert.equal(login.data.user.email, 'desktop@example.test');
  assert.match(login.data.token, /^[a-f0-9]{64}$/);
  assert.ok(login.data.expiresAt > Date.now());
  assert.equal(login.cookie, null);

  const strategies = ['众志合一', '旧赛季策略'];
  const publication = share.replace('"Description":', '"AvailableStrategies":' + JSON.stringify(strategies) + ',"Description":');
  const payload = { localId: '82bbca5f-37a8-49c0-9b46-3a03d34a745d', share: publication };
  const publish = (token) => desktop.request('/api/desktop/lineups', payload, null, 'POST', token);
  assert.equal((await publish()).status, 401);
  const web = browser();
  assert.equal((await web.login('web@example.test')).status, 200);
  assert.equal((await web.request('/api/desktop/lineups', payload)).status, 401);
  const result = await Promise.all([publish(login.data.token), publish(login.data.token)]);
  assert.deepEqual(result.map(item => item.status).sort(), [200, 201]);
  assert.equal(result[0].data.id, result[1].data.id);
  const detailPath = '/api/lineups/' + result[0].data.id;
  const detail = await desktop.request(detailPath);
  assert.deepEqual(JSON.parse(detail.data.lineup.share.split('\n')[1]).AvailableStrategies, strategies);
  assert.equal(detail.data.canEdit, false);
  await DB.prepare("DELETE FROM auth_limits WHERE key LIKE 'cooldown:%'").run();
  assert.equal((await web.login('desktop@example.test')).status, 200);
  assert.equal((await web.request(detailPath)).data.canEdit, true);

  assert.equal((await desktop.request('/api/auth/desktop/logout', {}, null, 'POST', login.data.token)).status, 200);
  assert.equal((await publish(login.data.token)).status, 401);
});
