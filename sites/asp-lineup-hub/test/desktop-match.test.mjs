import test from 'node:test';
import assert from 'node:assert/strict';
import { randomBytes, randomUUID } from 'node:crypto';
import worker, { DesktopMatchService } from '../dist/server/index.js';

const ticket = () => randomBytes(32).toString('hex');
const strategies = ['众志合一', '坚不可摧', '加急调派', '食腐之蝶', '统御号令'];
const profile = (nickname, tactic = '随便', strategy = strategies[0], covenant = '随便') =>
  ({ nickname, tactic, strategy, covenant });

function setup(t) {
  let time = 1_800_000_000_000;
  const env = { matchService: new DesktopMatchService(() => time) };
  const advance = ms => { time += ms; };
  async function request(token, action, body = {}) {
    const response = await worker.fetch(new Request('https://example.test/api/desktop/match/' + action, {
      method: 'POST', headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    }), env);
    return { status: response.status, data: await response.json() };
  }
  async function ok(...args) {
    const { status, data } = await request(...args);
    assert.equal(status, 200, JSON.stringify(data));
    return data;
  }
  const tokens = Array.from({ length: 4 }, ticket);
  async function group() {
    for (let i = 0; i < 4; i++) await ok(tokens[i], 'join', profile('玩家' + i, '随便', strategies[i]));
    return ok(tokens[0], 'state');
  }
  async function creating() {
    await group();
    const confirmed = await Promise.all(tokens.map(token => ok(token, 'confirm')));
    const room = await ok(tokens[0], 'state');
    assert.ok(confirmed.some(s => s.phase === 'creating'));
    const ids = await Promise.all(tokens.map(async token => (await ok(token, 'state')).selfId));
    const captain = tokens[ids.indexOf(room.captainId)];
    return { captain, others: tokens.filter(token => token !== captain) };
  }
  async function playing() {
    const { captain, others } = await creating();
    await ok(captain, 'invite', { invitation: '测试邀请' });
    for (const token of others) await ok(token, 'ready');
    return { captain, others };
  }
  return { request, ok, tokens, group, creating, playing, advance, env };
}

test('未知凭证的状态已结束，公开成员 ID 不能用作身份凭证', async t => {
  const { request, ok, tokens } = setup(t);
  assert.equal((await ok(ticket(), 'state')).phase, 'ended');
  const { selfId } = await ok(tokens[0], 'join', profile('测试'));
  assert.notEqual(selfId, tokens[0]);
  assert.equal((await request(selfId, 'leave')).status, 401);
  assert.equal((await ok(tokens[0], 'state')).phase, 'queued');
});

test('只能提交目录中的策略及盟约，策略须明确选择', async t => {
  const { request, ok, tokens } = setup(t);
  const token = tokens[0];
  for (const data of [profile('测试', '随便', '随便'), profile('测试', '随便', '未知策略'),
    profile('测试', '随便', '众志合一', '未知盟约'), profile('测试', '未填写')]) {
    assert.equal((await request(token, 'join', data)).status, 400);
  }
  assert.equal((await ok(token, 'join', profile('测试', '攻坚', '众志合一', '协防干员'))).phase, 'queued');
  const defaults = await ok(tokens[1], 'join', { nickname: '博士', strategy: '众志合一' });
  assert.equal(defaults.members[0].tactic, '随便');
  assert.equal(defaults.members[0].covenant, '随便');
});

test('优先等待战术二比二、策略与盟约各异的队友，随便可补位', async t => {
  const { ok } = setup(t);
  const tokens = Array.from({ length: 6 }, ticket);
  const profiles = [
    profile('甲', '兜底', strategies[0], '炎'), profile('乙', '兜底', strategies[0], '谢拉格'),
    profile('丙', '攻坚', strategies[1], '炎'), profile('丁', '随便', strategies[2], '维多利亚'),
    profile('戊', '兜底', strategies[3], '卡西米尔'), profile('己', '攻坚', strategies[4], '莱塔尼亚')
  ];
  for (let i = 0; i < tokens.length; i++) {
    const state = await ok(tokens[i], 'join', profiles[i]);
    if (i < 4) assert.equal(state.phase, 'queued');
  }
  // 乙、丙、丁、戊是第一个完全兼容的四人组。
  const state = await ok(tokens[1], 'state');
  assert.equal(state.phase, 'confirming');
  assert.deepEqual(state.members.map(m => m.nickname).sort(), ['乙','丙','丁','戊'].sort());
  assert.equal(state.members.find(m => m.nickname === '丁').assignedTactic, '攻坚');
  assert.equal(state.deadline - state.serverNow, 20_000);
  assert.equal(new Set(state.members.map(m => m.color)).size, 4);
  assert.ok(state.members.every(m => /^#[A-F0-9]{6}$/i.test(m.color)));
  assert.ok(tokens.every(token => !JSON.stringify(state).includes(token)));
});

test('两类偏好各有四个名额，队员可查看完整列表，旧版单选仍可加入', async t => {
  const { ok, request, tokens } = setup(t);
  const covenants = ['炎', '萨尔贡', '谢拉格', '维多利亚'];
  const body = { nickname: '队长', strategies: strategies.slice(0, 4), covenants, invitation: '房间A' };
  for (const change of [{ strategies: [] }, { strategies }, { covenants: [...covenants, '乌萨斯'] },
    { strategies: [strategies[0], strategies[0]] }, { covenants: ['随便', '炎'] },
    { strategies: [strategies[0], '未知'] }, { covenants: '炎' }]) {
    assert.equal((await request(tokens[0], 'join-room', { ...body, ...change })).status, 400);
  }
  await ok(tokens[0], 'join-room', body);
  const room = await ok(tokens[1], 'join-room', { ...profile('队员'), invitation: body.invitation });
  const [captain, guest] = room.members;
  assert.deepEqual(captain.strategies, body.strategies);
  assert.deepEqual(captain.covenants, covenants);
  assert.equal(captain.strategy, body.strategies.join('、'));
  assert.equal(captain.covenant, covenants.join('、'));
  assert.deepEqual(guest.strategies, [strategies[0]]);
  assert.deepEqual(guest.covenants, []);
  assert.equal(guest.covenant, '随便');
});

test('仅第二项策略或盟约重合也延迟成组，超时后仍可成局', async t => {
  for (const field of ['strategies', 'covenants']) {
    const { ok, tokens, advance } = setup(t);
    const profiles = tokens.map((_, i) => ({ nickname: String(i), strategies: [strategies[i]], covenants: [] }));
    profiles[0][field].push(field === 'strategies' ? strategies[4] : '炎');
    profiles[1][field].push(field === 'strategies' ? strategies[4] : '炎');
    for (let i = 0; i < 4; i++) assert.equal((await ok(tokens[i], 'join', profiles[i])).phase, 'queued');
    advance(60_000);
    assert.equal((await ok(tokens[0], 'state')).phase, 'confirming');
  }
});

test('四名偏好冲突的玩家等待一分钟后仍可成组', async t => {
  const { ok, tokens, advance } = setup(t);
  for (const token of tokens) await ok(token, 'join', profile('玩家', '兜底', '众志合一', '炎'));
  advance(59_999);
  assert.equal((await ok(tokens[0], 'state')).phase, 'queued');
  advance(1);
  assert.equal((await ok(tokens[0], 'state')).phase, 'confirming');
});

test('放宽条件仍优先带上最久等待者并补足二比二战术', async t => {
  const { ok, advance } = setup(t);
  const tokens = Array.from({ length: 6 }, ticket);
  for (let i = 0; i < tokens.length; i++) {
    await ok(tokens[i], 'join', profile('玩家' + i, i < 4 ? '兜底' : '攻坚'));
  }
  advance(60_000);
  const group = await ok(tokens[0], 'state');
  assert.deepEqual(group.members.map(m => m.nickname), ['玩家0', '玩家1', '玩家4', '玩家5']);
  assert.equal((await ok(tokens[2], 'state')).phase, 'queued');
});

test('并发重试只入队一次，掉线票据或重启后查状态都能正常结束', async t => {
  const { ok, tokens, advance, env } = setup(t);
  const joins = await Promise.all(Array.from({ length: 4 }, () => ok(tokens[0], 'join', profile('同一人'))));
  assert.equal(new Set(joins.map(state => state.selfId)).size, 1);
  assert.ok(joins.every(state => state.phase === 'queued'));
  advance(90_000);
  assert.equal((await ok(tokens[0], 'state')).phase, 'ended');
  advance(3_600_000);
  assert.equal((await ok(tokens[0], 'join', profile('延迟重试'))).phase, 'ended');
  await ok(tokens[1], 'join', profile('另一人'));
  env.matchService = new DesktopMatchService();
  assert.equal((await ok(tokens[1], 'state')).phase, 'ended');
});

test('确认倒计时由服务端执行，重试旧 join 不会把玩家重新放入队列', async t => {
  const { ok, tokens, group, advance } = setup(t);
  await group();
  await ok(tokens[0], 'confirm');
  advance(20_000);
  assert.equal((await ok(tokens[1], 'confirm')).phase, 'ended');
  for (const token of tokens) assert.equal((await ok(token, 'join', profile('重试'))).phase, 'ended');
  const late = ticket();
  await ok(late, 'leave');
  assert.equal((await ok(late, 'join', profile('延迟的请求'))).phase, 'ended');
});

test('开战前任意一人离开则整队退出，停止轮询的房员亦触发退出', async t => {
  const { group, ok, tokens } = setup(t);
  await group();
  await ok(tokens[2], 'leave');
  assert.equal((await ok(tokens[0], 'state')).phase, 'ended');
  const game = setup(t);
  await game.creating();
  game.advance(50_000);
  for (const token of game.tokens.slice(1)) await game.ok(token, 'state');
  game.advance(41_000);
  assert.equal((await game.ok(game.tokens[1], 'state')).phase, 'ended');
});

test('队长提交邀请后自动就绪，另外三人分别确认进入游戏', async t => {
  const { request, ok, creating } = setup(t);
  const { captain, others } = await creating();
  assert.equal((await request(others[0], 'invite', { invitation: '伪造' })).status, 403);
  const joining = await ok(captain, 'invite', { invitation: '  房间邀请\n测试  ' });
  assert.equal(joining.phase, 'joining');
  assert.equal(joining.invitation, '房间邀请\n测试');
  assert.deepEqual(joining.members.filter(m => m.ready).map(m => m.id), [joining.captainId]);
  await ok(captain, 'invite', { invitation: '房间邀请\n测试' });
  await ok(others[0], 'ready');
  await ok(others[0], 'ready');
  assert.equal((await ok(others[1], 'ready')).phase, 'joining');
  assert.equal((await ok(others[2], 'ready')).phase, 'playing');
  assert.equal((await ok(captain, 'leave')).phase, 'ended');
  assert.equal((await ok(others[0], 'state')).members.length, 3);
});

test('画笔按自己和队友的屏蔽关系分发，客户端可略过缓存的坐标，十秒后清除', async t => {
  const { request, ok, playing, tokens, advance } = setup(t);
  await playing();
  const [a,b,c,d] = tokens;
  const state = await ok(a, 'state');
  const peerIds = state.members.map(m => m.id).filter(id => id !== state.selfId);
  const point = { x: 0.12, y: 1 };
  const stroke = { id: randomUUID(), points: [point] };
  assert.equal((await request(a, 'stroke', { ...stroke, points: [{ x: -1, y: 0 }] })).status, 400);
  assert.equal((await request(a, 'stroke', { ...stroke, points: Array(513).fill(point) })).status, 400);
  await ok(a, 'stroke', stroke);
  await ok(a, 'stroke', stroke);
  const expected = [{ ...stroke, playerId: state.selfId, expiresAt: state.serverNow + 10_000 }];
  assert.deepEqual((await ok(b, 'state')).strokes, expected);
  assert.deepEqual((await ok(b, 'state', { knownStrokes: [stroke.id] })).strokes, [{ ...expected[0], points: [] }]);
  await ok(b, 'mute', { muted: [state.selfId] });
  assert.deepEqual((await ok(b, 'state')).strokes, []);
  assert.deepEqual((await ok(c, 'state')).strokes, expected);
  for (const token of [c,d]) await ok(token, 'mute', { muted: [state.selfId] });
  const second = { ...stroke, id: randomUUID() };
  assert.ok((await ok(a, 'stroke', second)).strokes.some(s => s.id === second.id));
  assert.deepEqual((await ok(c, 'state')).strokes, []);
  await ok(a, 'mute', { muted: peerIds });
  assert.equal((await request(a, 'stroke', { ...stroke, id: randomUUID() })).status, 409);
  advance(10_000);
  assert.deepEqual((await ok(d, 'state')).strokes, []);
  await ok(a, 'mute', { muted: [] });
  assert.deepEqual((await ok(a, 'stroke', stroke)).strokes, []);
  for (const token of [b,c,d]) await ok(token, 'leave');
  assert.equal((await request(a, 'stroke', { ...stroke, id: randomUUID() })).status, 409);
});

test('相同分享文本创建一支四人队伍，首位是房主，集齐并就位后共用游戏房间', async t => {
  const { ok, request, tokens } = setup(t);
  const join = (token, invitation = '卫戍协议\n房间 A') =>
    ok(token, 'join-room', { ...profile('博士'), invitation });
  const host = await join(tokens[0], '  卫戍协议\r\n房间 A  ');
  assert.equal(host.phase, 'joining');
  assert.equal(host.captainId, host.selfId);
  assert.equal(host.members[0].ready, true);
  assert.equal(host.invitation, '卫戍协议\n房间 A');
  const guest = await join(tokens[1]);
  assert.equal(guest.roomId, host.roomId);
  assert.equal(guest.members[0].color, host.members[0].color);
  assert.equal((await ok(tokens[1], 'ready')).phase, 'joining');
  const arrivals = await Promise.all([join(tokens[2]), join(tokens[3]), join(tokens[1])]);
  assert.ok(arrivals.every(s => s.roomId === host.roomId));
  const group = await ok(tokens[0], 'state');
  assert.equal(group.members.length, 4);
  assert.equal(new Set(group.members.map(m => m.color)).size, 4);
  assert.equal(group.members.filter(m => m.assignedTactic === '兜底').length, 2);
  const extra = ticket();
  const body = { ...profile('第五人'), invitation: host.invitation };
  assert.equal((await request(extra, 'join-room', body)).status, 409);
  await ok(tokens[2], 'ready');
  assert.equal((await ok(tokens[3], 'ready')).phase, 'playing');
  const stroke = { id: randomUUID(), points: [{ x: 0.25, y: 0.5 }] };
  await ok(tokens[0], 'stroke', stroke);
  assert.equal((await ok(tokens[2], 'state')).strokes[0].id, stroke.id);
  assert.deepEqual((await ok(tokens[2], 'mute', { muted: [host.selfId] })).strokes, []);
  await ok(tokens[0], 'leave');
  assert.equal((await request(extra, 'join-room', body)).status, 409);
  for (const token of tokens.slice(1)) await ok(token, 'leave');
  const next = await join(extra);
  assert.notEqual(next.roomId, host.roomId);
  assert.equal(next.captainId, next.selfId);
});

test('分享文本彼此隔离，退出或掉线释放手动房间，重试旧票据不会占新队伍席位', async t => {
  const { ok, request, advance, tokens } = setup(t);
  const join = (token, invitation) => ok(token, 'join-room', { ...profile('博士'), invitation });
  for (const invitation of ['', '  ', 'A'.repeat(1025)]) {
    assert.equal((await request(tokens[0], 'join-room', { ...profile('博士'), invitation })).status, 400);
  }
  const a = await join(tokens[0], '邀请 A');
  const b = await join(tokens[1], '邀请 B');
  assert.notEqual(a.roomId, b.roomId);
  await join(tokens[2], '邀请 A');
  await ok(tokens[2], 'leave');
  assert.equal((await join(tokens[0], '邀请 A')).phase, 'ended');
  assert.equal((await ok(tokens[1], 'state')).phase, 'joining');
  assert.equal((await ok(tokens[3], 'join', profile('排队玩家'))).phase, 'queued');
  advance(89_999);
  await ok(ticket(), 'state'); // Sweep just before the original host's heartbeat expires.
  advance(1);
  const reopened = await join(ticket(), '邀请 B');
  assert.notEqual(reopened.roomId, b.roomId);
  assert.equal(reopened.members.length, 1);
});

test('自建队伍的队长能带现有成员强制开始，开局后保持成员和笔迹同步', async t => {
  for (const size of [1, 2, 3, 4]) {
    const { ok, request, tokens } = setup(t);
    const body = { ...profile('队员'), invitation: '强制开始测试' };
    const host = await ok(tokens[0], 'join-room', body);
    assert.equal(host.canForceStart, true);
    for (const token of tokens.slice(1, size)) {
      const guest = await ok(token, 'join-room', body);
      assert.equal(guest.canForceStart, false);
      assert.equal((await request(token, 'force-start')).status, 403);
    }
    const starting = await ok(tokens[0], 'force-start');
    assert.equal(starting.phase, 'playing');
    assert.equal(starting.canForceStart, false);
    assert.equal(starting.roomId, host.roomId);
    assert.equal(starting.members.length, size);
    const defense = starting.members.filter(m => m.assignedTactic === '兜底').length;
    const attack = starting.members.filter(m => m.assignedTactic === '攻坚').length;
    assert.equal(defense + attack, size);
    assert.ok(Math.abs(defense - attack) <= 1);
    assert.deepEqual((await ok(tokens[0], 'force-start')).members, starting.members);
    assert.equal((await request(ticket(), 'join-room', body)).status, 409);
    if (size > 1) {
      assert.equal((await ok(tokens[1], 'state')).phase, 'playing');
      assert.equal((await ok(tokens[1], 'ready')).phase, 'playing');
      const stroke = { id: randomUUID(), points: [{ x: .2, y: .8 }] };
      await ok(tokens[0], 'stroke', stroke);
      assert.equal((await ok(tokens[1], 'state')).strokes[0].id, stroke.id);
    }
    for (const token of tokens.slice(0, size)) await ok(token, 'leave');
    assert.notEqual((await ok(ticket(), 'join-room', body)).roomId, host.roomId);
  }
});

test('匹配队伍只有提交邀请后的队长可以强制开始', async t => {
  const { ok, request, creating } = setup(t);
  const { captain, others } = await creating();
  assert.equal((await request(captain, 'force-start')).status, 409);
  assert.equal((await ok(captain, 'invite', { invitation: '房间' })).canForceStart, true);
  assert.equal((await request(others[0], 'force-start')).status, 403);
  assert.equal((await ok(captain, 'force-start')).phase, 'playing');
});
