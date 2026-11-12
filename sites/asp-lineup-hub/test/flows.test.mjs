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
  const SHARE_SECRET = crypto.getRandomValues(new Uint8Array(32));
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
      }), { DB, AUTH, SHARE_SECRET, clientIp: '192.0.2.1' });
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
  return { DB, SHARE_SECRET, delivered, browser };
}

test('访客可创建和打开加密分享快照，快照不会发布到阵容列表', async (t) => {
  const { DB, SHARE_SECRET, browser } = setup();
  t.after(() => DB.close());
  const sender = browser(), recipient = browser();
  const list = await recipient.request('/api/lineups');
  const created = await sender.request('/api/share', { share }, null);
  assert.equal(created.status, 201);
  const { shortPath } = created.data;
  assert.match(shortPath, /^\/s\/u-[A-Za-z0-9_-]{22}$/);
  const apiPath = shortPath.replace('/s/', '/api/share/');
  assert.deepEqual((await recipient.request(apiPath)).data, { share, shortPath });
  const preview = (await recipient.request(apiPath + '?preview=1')).data.lineup;
  assert.equal(preview.title, '测试银灰阵容');
  assert.equal(preview.members[0].name, '凛御银灰');
  assert.equal(preview.shortPath, shortPath);
  const page = await worker.fetch(new Request('https://lineup.example.test' + shortPath), { DB, SHARE_SECRET });
  assert.equal(page.status, 302);
  assert.equal(page.headers.get('location'), '/?share=' + shortPath.slice(3));
  assert.equal((await recipient.request('/api/lineups')).data.total, list.data.total);

  const stored = await DB.prepare('SELECT payload FROM share_snapshots WHERE token = ?').bind(shortPath.slice(3)).first();
  assert.ok(stored.payload instanceof Uint8Array);
  assert.ok(!Buffer.from(stored.payload).includes(Buffer.from('测试银灰阵容')));
  const wrongKey = await worker.fetch(new Request('https://lineup.example.test' + apiPath),
    { DB, SHARE_SECRET: crypto.getRandomValues(new Uint8Array(32)) });
  assert.equal(wrongKey.status, 404);
  await DB.prepare('UPDATE share_snapshots SET expires_at = 0').run();
  assert.equal((await recipient.request(apiPath)).status, 404);
});

test('分享接口校验内容、限制创建频率，并拒绝跨站创建', async (t) => {
  const { DB, browser } = setup();
  t.after(() => DB.close());
  const guest = browser();
  assert.equal((await guest.request('/api/share', { share: 'bad' })).status, 400);
  assert.equal((await guest.request('/api/share', { share }, 'https://other.test')).status, 403);
  let status;
  for (let i = 0; i < 32 && status !== 429; i++) status = (await guest.request('/api/share', { share })).status;
  assert.equal(status, 429);
});

test('暴躁铁皮的四个装备槽随分享保留，普通干员无法使用第三槽', async (t) => {
  const { DB, browser } = setup();
  t.after(() => DB.close());
  const guest = browser();
  const { data: catalog } = await guest.request('/api/editor-catalog');
  const name = '“暴躁铁皮”';
  assert.equal(catalog.operators[name]?.equipmentSlotCount, 4);
  const names = Object.keys(catalog.equipment).slice(0, 4);
  const member = Object.fromEntries(names.map((item, index) => ['Equipment' + (index + 1), item]));
  const encode = OperatorName => 'ASPA-LINEUP/2\n' + JSON.stringify({
    Name: '四槽阵容', Author: '测试', Members: [{ OperatorName, ...member }]
  });
  const text = encode(name);
  const created = await guest.request('/api/share', { share: text });
  assert.equal(created.status, 201);
  const apiPath = created.data.shortPath.replace('/s/', '/api/share/');
  assert.equal((await guest.request(apiPath)).data.share, text);
  assert.deepEqual((await guest.request(apiPath + '?preview=1')).data.lineup.members[0].equipment, names);
  assert.equal((await guest.request('/api/share', { share: encode('折光') })).status, 400);
});

test('自选干员由独立接口提供，也可用于短链接、头像和精锐盟约', async (t) => {
  const { DB, browser } = setup();
  t.after(() => DB.close());
  const guest = browser();
  const { data: base } = await guest.request('/api/editor-catalog');
  assert.equal(base.operators['银灰'], undefined);
  const { status, data: optional } = await guest.request('/api/self-selected-operators');
  assert.equal(status, 200);
  assert.equal(optional['银灰'].tier, 0);
  assert.deepEqual(optional['银灰'].covenants, ['谢拉格']);
  assert.deepEqual(optional['维什戴尔'].additionalCovenants, ['协防干员']);
  const votePath = '/api/operator-votes?season=act3autochess&operator=' + encodeURIComponent('银灰');
  assert.equal((await guest.request(votePath)).status, 200);
  await guest.login('self-selected-voter@example.test');
  assert.equal((await guest.request(votePath, { skill: 3 })).status, 201);
  assert.equal((await guest.request(votePath, { equipment: [Object.keys(base.equipment)[0]] })).status, 201);
  assert.deepEqual((await guest.request(votePath.replace('operator-votes?', 'operator-votes/mine?'))).data,
    { skillVoted: true, equipmentVoted: true });
  const portrait = await worker.fetch(new Request('https://lineup.example.test' + optional['银灰'].image), { DB });
  assert.equal(portrait.status, 200);
  assert.equal(portrait.headers.get('content-type'), 'image/png');
  const share = 'ASPA-LINEUP/2\n' + JSON.stringify({
    Name: '自选阵容', Author: '测试',
    Members: ['银灰', '维什戴尔'].map(OperatorName => ({ OperatorName, IsElite: true }))
  });
  const created = await guest.request('/api/share', { share });
  assert.equal(created.status, 201);
  const path = created.data.shortPath.replace('/s/', '/api/share/');
  const { data: preview } = await guest.request(path + '?preview=1');
  assert.ok(preview.lineup.members.every(member => member.tier === 0 && member.elite));
  assert.ok(preview.lineup.activeCovenants.includes('绝技'));
});

test('阵容最多容纳三十名干员，公开阵容可以按手选盟约筛选', async (t) => {
  const { DB, browser } = setup();
  t.after(() => DB.close());
  const player = browser();
  await player.login('covenants@example.test');
  const { data: catalog } = await player.request('/api/editor-catalog');
  const members = Object.keys(catalog.operators).slice(0, 31).map(OperatorName => ({ OperatorName }));
  const encode = count => 'ASPA-LINEUP/2\n' + JSON.stringify({
    Name: '三十人阵容', Author: '测试', CovenantTags: ['炎', '迅捷'], Members: members.slice(0, count)
  });
  const captcha = await player.challenge('publish');
  assert.equal((await player.request('/api/lineups', { share: encode(31), captcha })).status, 400);
  const result = await player.request('/api/lineups', { share: encode(30), captcha });
  assert.equal(result.status, 201);
  const { lineup } = (await player.request('/api/lineups/' + result.data.id)).data;
  assert.equal(lineup.members.length, 30);
  assert.deepEqual(lineup.covenants, ['炎', '迅捷']);
  assert.deepEqual(JSON.parse(lineup.share.split('\n')[1]).CovenantTags, ['炎', '迅捷']);
  const filtered = await player.request('/api/lineups?covenant=' + encodeURIComponent('迅捷'));
  assert.ok(filtered.data.items.some(item => item.id === result.data.id));
});

test('卡片和短链预览从装备、调和和精锐状态计算已激活盟约', async (t) => {
  const { DB, browser } = setup();
  t.after(() => DB.close());
  const player = browser();
  await player.login('activation@example.test');
  const content = {
    Name: '盟约判定验收', Author: '测试', CovenantTags: ['莱塔尼亚'],
    Members: [
      { OperatorName: '黍', IsElite: true, Equipment1: '变形同构体', Equipment2: '炎国短刀' },
      { OperatorName: '余', IsElite: true },
      { OperatorName: '魔王' },
      { OperatorName: '盟约·辅助干员' },
      { OperatorName: '艾丝黛尔' },
      { OperatorName: '伊桑', Equipment1: '变形同构体', Equipment2: '精准狙击镜' },
      { OperatorName: '“暴躁铁皮”', Equipment3: '变形同构体', Equipment4: '精准狙击镜' }
    ]
  };
  const encode = () => 'ASPA-LINEUP/2\n' + JSON.stringify(content);
  const created = await player.request('/api/lineups', { share: encode(), captcha: await player.challenge('publish') });
  assert.equal(created.status, 201);
  const { items } = (await player.request('/api/lineups?q=' + encodeURIComponent(content.Name))).data;
  assert.deepEqual(items[0].activeCovenants, ['炎', '精准', '调和', '独行', '绝技']);
  assert.deepEqual(items[0].covenants, ['莱塔尼亚']);
  const snapshot = await player.request('/api/share', { share: encode() });
  const path = snapshot.data.shortPath.replace('/s/', '/api/share/');
  const { lineup } = (await player.request(path + '?preview=1')).data;
  assert.deepEqual(lineup.activeCovenants, items[0].activeCovenants);
  assert.equal(JSON.parse(lineup.share.split('\n')[1]).Members[0].IsElite, true);

  content.Members = [
    content.Members[0], { OperatorName: '余' }, { OperatorName: '地灵' }, { OperatorName: '艾丝黛尔' },
    { OperatorName: '伊桑', Equipment1: '变形同构体' },
    { OperatorName: '“暴躁铁皮”', Equipment3: '精准狙击镜' }
  ];
  const split = await player.request('/api/share', { share: encode() });
  assert.equal(split.status, 201);
  const splitPath = split.data.shortPath.replace('/s/', '/api/share/');
  assert.deepEqual((await player.request(splitPath + '?preview=1')).data.lineup.activeCovenants, []);
});

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
    Name: '双核心', Author: '测试', Description: '说明 {{strategy:众志合一}}\n\n```lineup\nA[{{op:凛御银灰}}]\n%% aspa-layout A 211 88\n```',
    AvailableStrategies: ['众志合一', '旧赛季策略', '坚不可摧', '加急调派', '幻画为真', '全力熔铸'],
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
  assert.deepEqual(lineup.strategies, content.AvailableStrategies);
  assert.deepEqual(lineup.strategyPortraits.map(({ name }) => name), content.AvailableStrategies);
  assert.match(lineup.strategyPortraits[0].image, /^\/strategy-icons\/[a-f0-9]{16}\.png$/);
  assert.equal(lineup.strategyPortraits[1].image, null);
  const { items } = (await player.request('/api/lineups?q=' + encodeURIComponent(content.Name))).data;
  assert.equal(items[0].strategyCount, 6);
  assert.deepEqual(items[0].strategyPortraits, lineup.strategyPortraits.slice(0, 5));
});

test('发布后生成可匿名解析的短链接，并兼容示例阵容', async () => {
  const { browser } = setup();
  const player = browser(), guest = browser();
  await player.login('links@example.test');
  const posted = await player.request('/api/lineups', { share, captcha: await player.challenge('publish') });
  const { id, shortPath } = posted.data;
  assert.match(shortPath, /^\/s\/[A-Za-z0-9_-]{22}$/);
  assert.deepEqual((await guest.request(shortPath.replace('/s/', '/api/share/'))).data, { share, shortPath });
  assert.equal((await guest.request('/api/lineups/' + id)).data.lineup.shortPath, shortPath);
  const demo = (await guest.request('/api/share/demo-kjerag')).data;
  assert.equal(demo.shortPath, '/s/demo-kjerag');
  assert.match(demo.share, /^ASPA-LINEUP\/2/);
  assert.equal((await guest.request('/api/share/AAAAAAAAAAAAAAAAAAAAAA')).status, 404);
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
test('干员投票对访客公开，对同一账号的每种投票只记一次', async (t) => {
  const { DB, delivered, browser } = setup();
  t.after(() => DB.close());
  const guest = browser(), desktop = browser(), web = browser();
  const path = '/api/operator-votes?season=act3autochess&operator=' + encodeURIComponent('黑键');
  const mine = path.replace('operator-votes?', 'operator-votes/mine?');
  const vote = (body) => web.request(path, body);
  assert.deepEqual((await guest.request(path)).data, { skills: [], equipment: [] });
  assert.equal((await guest.request(mine)).status, 401);
  assert.equal((await guest.request(path, { skill: 2 })).status, 401);
  assert.equal((await guest.request(path.replace('act3autochess', 'old'))).status, 404);

  const sent = await desktop.send('poll@example.test');
  const { data: { token } } = await desktop.request('/api/auth/desktop/verify',
    { id: sent.data.id, code: delivered.at(-1).code }, null);
  await DB.prepare("DELETE FROM auth_limits WHERE key LIKE 'cooldown:%'").run();
  assert.equal((await web.login('poll@example.test')).status, 200);

  assert.equal((await vote({ skill: 4 })).status, 400);
  assert.equal((await vote({ equipment: ['不存在'] })).status, 400);
  assert.equal((await vote({ equipment: ['雪祀之衣', '雪祀之衣'] })).status, 400);
  assert.equal((await web.request(path, { skill: 2 }, 'https://other.example')).status, 403);
  assert.deepEqual((await web.request(mine)).data, { skillVoted: false, equipmentVoted: false });
  const parallel = await Promise.all([vote({ skill: 2 }), desktop.request(path, { skill: 1 }, null, 'POST', token)]);
  assert.deepEqual(parallel.map(result => result.status).sort(), [200, 201]);
  assert.deepEqual((await desktop.request(mine, null, null, 'GET', token)).data, {
    skillVoted: true, equipmentVoted: false
  });
  assert.equal((await vote({ equipment: ['雪祀之衣', '信标'] })).status, 201);
  assert.equal((await vote({ equipment: ['雪祀之衣'] })).status, 200);
  assert.deepEqual((await web.request(mine)).data, { skillVoted: true, equipmentVoted: true });

  // Read the persisted totals directly while the public response is still cached.
  const { results } = await DB.prepare('SELECT kind, choice, votes FROM operator_vote_totals ORDER BY kind, choice').all();
  assert.equal(results.length, 3);
  assert.ok(results.every(row => row.votes === 1));
});

test('装备只公开前五，短时间内重复查看共用汇总缓存', async (t) => {
  const { DB, SHARE_SECRET, browser } = setup();
  t.after(() => DB.close());
  const choices = ['雪祀之衣', '信标', '莱塔尼亚小鼓', '聚合急救箱', '盟约之币', '奥术法阵'];
  for (const [index, name] of choices.entries()) {
    await DB.prepare('INSERT INTO operator_vote_totals VALUES (?, ?, ?, ?, ?)')
      .bind('act3autochess', '森蚺', 'equipment', name, index + 1).run();
  }
  const path = '/api/operator-votes?season=act3autochess&operator=' + encodeURIComponent('森蚺');
  const result = await worker.fetch(new Request('https://lineup.example.test' + path), { DB, SHARE_SECRET });
  assert.match(result.headers.get('Cache-Control'), /public, max-age=/);
  assert.deepEqual((await result.json()).equipment, choices.slice(1).reverse().map((choice, index) =>
    ({ choice, votes: 6 - index })));
  await DB.prepare('DELETE FROM operator_vote_totals').run();
  assert.equal((await browser().request(path)).data.equipment.length, 5);
});
