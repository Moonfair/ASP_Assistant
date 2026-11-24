import test from 'node:test';
import assert from 'node:assert/strict';
import { randomBytes, randomUUID } from 'node:crypto';
import worker, { DesktopMatchService, LiveMatchService } from '../dist/server/index.js';

const ticket = () => randomBytes(32).toString('hex');
const tactics = ['众志合一', '坚不可摧', '加急调派', '食腐之蝶'];
const preferences = (strategy, tactic = '随便', covenants = []) =>
  ({ strategies: Array.isArray(strategy) ? strategy : [strategy], tactic, covenants });
const seat = uid => ({ uid, nickname: '博士' + uid.slice(-1), ready: true });
const game = (selfUid, roomId = '', hostUid = '', uids = [], phase = 'lobby') =>
  ({ selfUid, nickname: '', roomId, hostUid, phase: roomId ? phase : 'idle', members: uids.map(seat) });
const stroke = () => ({ id: randomUUID(), points: [{ x: .5, y: .25 }] });

function setup(syncDefaults = {}) {
  let now = 1_800_000_000_000;
  const env = { matchService: new DesktopMatchService(() => now), liveMatchService: new LiveMatchService(() => now) };
  async function request(key, action, body = {}) {
    const response = await worker.fetch(new Request('https://example.test/api/desktop/match/' + action, {
      method: 'POST', headers: { Authorization: 'Bearer ' + key, 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    }), env);
    return { status: response.status, data: await response.json() };
  }
  const live = (key, action, body) => request(key, 'live/' + action, body);
  async function ok(...args) {
    const { status, data } = await live(...args);
    assert.equal(status, 200, JSON.stringify(data));
    return data;
  }
  const sync = (key, state, options = {}) => ok(key, 'sync', { game: state, strategies: [], covenants: [], tactic: '随便', ...syncDefaults, ...options });
  async function publish(key, state, options = {}, invitation = '游戏邀请') {
    const { roomId } = await sync(key, state, options);
    return ok(key, 'recruit', { roomId, invitation });
  }
  return { request, live, ok, sync, publish, advance(ms) { now += ms; } };
}

test('以游戏中的实际名单建房，访客可先到，只有实际房主能发布招募', async () => {
  const { sync, live, ok } = setup();
  const a = ticket(), b = ticket(), uids = ['private-A', 'private-B', 'private-C'];
  const lobby = uid => game(uid, '1234', uids[0], uids);
  const observed = await sync(b, lobby(uids[1]), preferences(tactics[0]));
  assert.equal(observed.phase, 'lobby');
  assert.notEqual(observed.gameRoomId, observed.roomId);
  assert.equal(observed.gameRoomId, '1234');
  assert.deepEqual(observed.members.map(m => m.hasPlugin), [false, true, false]);
  assert.equal(observed.captainId, observed.members[0].id);
  assert.equal(observed.selfId, observed.members[1].id);
  assert.deepEqual(observed.members.filter(m => !m.hasPlugin).map(m => [m.strategy, m.covenant, m.tactic]),
    [['未知', '未知', '未知'], ['未知', '未知', '未知']]);
  assert.ok(!JSON.stringify(observed).includes('private-'));
  assert.equal((await live(b, 'recruit', { roomId: observed.roomId, invitation: 'x' })).status, 403);
  const host = await sync(a, lobby(uids[0]));
  assert.equal(host.selfId, observed.captainId);
  assert.equal(host.members[0].strategy, '未知');
  assert.equal(host.canForceStart, true);
  assert.equal((await live(a, 'recruit', { roomId: randomUUID(), invitation: 'x' })).status, 409);
  const published = await ok(a, 'recruit', { roomId: host.roomId, invitation: '  游戏邀请\r\n测试  ' });
  assert.equal(published.invitation, '游戏邀请\n测试');
  assert.equal(published.recruiting, true);
  assert.equal((await ok(a, 'stop-recruit', { roomId: host.roomId })).recruiting, false);
});

test('推荐只展示当前一个房间，多人看同一邀请均不占位，实际加入才成为队友', async () => {
  const { sync, publish, ok, live } = setup();
  const a = ticket(), b = ticket(), c = ticket();
  const h = await publish(a, game('A', '101', 'A', ['A']), preferences(tactics[0]));
  for (const [key, uid] of [[b, 'B'], [c, 'C']]) {
    await sync(key, game(uid), preferences(tactics[1]));
    const offer = await ok(key, 'search');
    assert.equal(offer.phase, 'offer');
    assert.equal(offer.roomId, h.roomId);
    assert.equal(offer.invitation, '游戏邀请');
    assert.equal(offer.members.length, 1);
    assert.ok(!offer.members.some(m => m.id === offer.selfId));
    assert.deepEqual(offer.strokes, []);
    assert.deepEqual(offer.shouts, []);
    assert.equal((await live(key, 'stroke', { roomId: h.roomId, ...stroke() })).status, 409);
    assert.equal((await live(key, 'shout', shout(h.roomId))).status, 409);
    assert.equal((await live(key, 'force-start', { roomId: h.roomId })).status, 409);
  }
  assert.equal((await sync(a, game('A', '101', 'A', ['A']))).members.length, 1);
  const joined = await sync(b, game('B', '101', 'A', ['A', 'B']));
  assert.equal(joined.phase, 'lobby');
  assert.equal(joined.roomId, h.roomId);
  assert.equal(joined.members.filter(m => m.hasPlugin).length, 2);
  assert.equal((await live(b, 'search')).status, 409);
  assert.equal((await ok(c, 'cancel-search')).phase, 'idle');
});

test('满员、匹配、开局、换房主、房主退出和房主心跳失效会撤销已展示的邀请', async () => {
  for (const change of ['full', 'matching', 'playing', 'host-change', 'leave', 'expired']) {
    const { sync, publish, ok, live, advance } = setup();
    const a = ticket(), b = ticket(), c = ticket(), lobby = uid => game(uid, 'L', 'A', ['A', 'B']);
    const host = await publish(a, lobby('A'));
    await sync(b, lobby('B'));
    await sync(c, game('C'));
    assert.equal((await ok(c, 'search')).phase, 'offer');
    if (change === 'leave') await ok(a, 'leave');
    else if (change === 'expired') { advance(12_000); await sync(b, lobby('B')); }
    else {
      const observed = change === 'full' ? game('B', 'L', 'A', ['A', 'B', 'D', 'E'])
        : change === 'host-change' ? game('B', 'L', 'B', ['A', 'B'])
        : game('B', change === 'playing' ? 'battle' : 'L', 'A', ['A', 'B'], change);
      await sync(b, observed);
      assert.equal((await sync(a, lobby('A'))).recruiting, false, change);
      assert.notEqual((await live(a, 'recruit', { roomId: host.roomId, invitation: '旧邀请' })).status, 200, change);
    }
    const withdrawn = await sync(c, game('C'));
    assert.equal(withdrawn.phase, 'searching', change);
    assert.ok(!withdrawn.invitation, change);
    const remaining = await sync(b, change === 'playing' ? game('B', 'battle', 'A', ['A', 'B'], 'playing') : lobby('B'));
    assert.ok(remaining.members.some(m => m.id === remaining.selfId && m.hasPlugin), change);
  }
});

test('搜索优先选择可达二比二且全部偏好无重复的队伍，等待一分钟放宽；换房与迟到重试不重复推荐', async () => {
  const { ok, sync, publish, advance } = setup();
  const seeker = ticket(), a = ticket(), b = ticket(), d = ticket(), z = ticket();
  const covenant = '炎';
  const hosts = [[a, game('A', 'A', 'A', ['A']), preferences([tactics[1], tactics[0]], '随便', [covenant])],
    [b, game('B', 'B', 'B', ['B']), preferences(tactics[2], '攻坚')],
    [d, game('D', 'D', 'D', ['D']), preferences(tactics[2], '兜底')]];
  const [conflict, balanced, other] = await Promise.all(hosts.map(args => publish(...args)));
  const idle = game('Z'), pref = preferences([tactics[3], tactics[0]], '兜底', [covenant]);
  await sync(seeker, idle, pref);
  assert.equal((await ok(seeker, 'search')).roomId, balanced.roomId);
  assert.equal((await ok(seeker, 'next', { roomId: balanced.roomId })).roomId, other.roomId);
  assert.equal((await ok(seeker, 'next', { roomId: balanced.roomId })).roomId, other.roomId);
  assert.equal((await ok(seeker, 'next', { roomId: other.roomId })).phase, 'searching');
  for (let i = 0; i < 5; i++) {
    advance(10_000);
    for (const args of hosts) await sync(...args);
  }
  advance(9999);
  assert.equal((await sync(seeker, idle, pref)).phase, 'searching');
  advance(1);
  assert.equal((await sync(seeker, idle, pref)).roomId, conflict.roomId);
  assert.equal((await ok(seeker, 'next', { roomId: conflict.roomId })).phase, 'searching');
  assert.equal((await sync(seeker, idle, pref)).phase, 'searching');
  await ok(seeker, 'cancel-search');
  assert.equal((await ok(seeker, 'search')).roomId, balanced.roomId);
  // Two attacking incumbents rank below an attainable 2:2, even after relaxation.
  await sync(z, game('guest', 'B', 'B', ['B', 'guest']), preferences(tactics[1], '攻坚'));
  await ok(d, 'stop-recruit', { roomId: other.roomId });
  await sync(seeker, idle, preferences(tactics[3], '攻坚'));
  assert.notEqual((await sync(seeker, idle, preferences(tactics[3], '攻坚'))).roomId, balanced.roomId);
  await ok(seeker, 'cancel-search');
  await sync(seeker, idle, preferences(tactics[3]));
  assert.equal((await ok(seeker, 'search')).roomId, balanced.roomId);
});

test('游戏单人排队可以没有房号，取消插件搜索或招募后直接进入实际对局', async () => {
  const { sync, publish, ok, live } = setup();
  const a = ticket(), b = ticket(), queue = uid => ({ ...game(uid), phase: 'matching', roomId: null, hostUid: null });
  const host = await publish(a, game('A', 'L', 'A', ['A']));
  await sync(b, game('B'));
  assert.equal((await ok(b, 'search')).phase, 'offer');
  for (const [key, uid] of [[a, 'A'], [b, 'B']]) {
    const waiting = await sync(key, queue(uid));
    assert.equal(waiting.phase, 'matching');
    assert.equal(waiting.roomId, null);
    assert.equal(waiting.gameRoomId, null);
    assert.ok(!waiting.invitation && !waiting.recruiting);
    assert.deepEqual(waiting.members, []);
    assert.equal((await live(key, 'search')).status, 409);
  }
  assert.equal((await live(a, 'recruit', { roomId: host.roomId, invitation: '旧房间' })).status, 409);
  const battle = uid => game(uid, 'new-game', '', ['A', 'B'], 'playing');
  const first = await sync(b, battle('B'));
  const second = await sync(a, battle('A'));
  assert.equal(first.roomId, second.roomId);
  assert.equal(second.members.filter(m => m.hasPlugin).length, 2);
});

test('持续招募满一分钟的房间能立即推荐给刚开始搜索的玩家，过期后需要房主重新发布', async () => {
  const { sync, publish, ok, advance } = setup();
  const a = ticket(), b = ticket(), lobby = game('A', 'L', 'A', ['A']);
  const prefs = preferences(tactics[0], '兜底');
  const posted = await publish(a, lobby, prefs);
  for (let n = 0; n < 6; n++) { advance(10_000); await sync(a, lobby, prefs); }
  await sync(b, game('B'), prefs);
  assert.equal((await ok(b, 'search')).roomId, posted.roomId);
  advance(12_000);
  assert.equal((await sync(a, lobby, prefs)).recruiting, false);
  assert.equal((await sync(b, game('B'), prefs)).phase, 'searching');
  assert.equal((await ok(a, 'recruit', { roomId: posted.roomId, invitation: '新的游戏邀请' })).recruiting, true);
});

test('游戏开局无需插件搜索，访客先看到对局号也能合并两个大厅，旧大厅轮询无法倒退或重新招募', async t => {
  t.mock.method(Math, 'random', () => 0);
  const { sync, publish, ok, live } = setup();
  const keys = Object.fromEntries(['A', 'B', 'C', 'D'].map(uid => [uid, ticket()]));
  const lobby1 = uid => game(uid, 'L1', 'A', ['A', 'B']);
  const lobby2 = uid => game(uid, 'L2', 'C', ['C', 'D']);
  const battle = uid => game(uid, 'G', '', ['A', 'B', 'C', 'D'], 'playing');
  const one = await publish(keys.A, lobby1('A'));
  const two = await publish(keys.C, lobby2('C'));
  const b = await sync(keys.B, lobby1('B'));
  const d = await sync(keys.D, lobby2('D'));
  const first = await sync(keys.D, battle('D'));
  assert.equal(first.roomId, two.roomId);
  assert.equal(first.gameRoomId, 'G');
  assert.equal(first.recruiting, false);
  assert.equal(first.members.filter(m => m.hasPlugin).length, 4);
  assert.equal(new Set(first.members.map(m => m.color)).size, 4);
  for (const member of d.members) assert.equal(first.members.find(s => s.id === member.id).color, member.color);
  assert.deepEqual(new Set(first.members.map(m => m.id)), new Set([one.selfId, b.selfId, two.selfId, d.selfId]));
  for (const [uid, stale] of [['A', lobby1], ['B', lobby1], ['C', lobby2]]) {
    const view = await sync(keys[uid], stale(uid));
    assert.equal(view.phase, 'playing');
    assert.equal(view.roomId, first.roomId);
    assert.equal((await live(keys[uid], 'recruit', { roomId: view.roomId, invitation: '过期' })).status >= 400, true);
    assert.equal((await sync(keys[uid], battle(uid))).roomId, first.roomId);
  }
  const line = stroke();
  await ok(keys.A, 'stroke', { roomId: first.roomId, ...line });
  assert.equal((await sync(keys.C, battle('C'))).strokes[0].id, line.id);
  const next = await sync(keys.A, lobby1('A'));
  assert.equal(next.phase, 'lobby');
  assert.notEqual(next.roomId, first.roomId);
  assert.equal((await sync(keys.B, battle('B'))).phase, 'playing');
});

test('房间从可用颜色中随机分配，每次轮询和队友进出后仍保留原有颜色', async t => {
  let roll = 0;
  t.mock.method(Math, 'random', () => roll);
  const { sync } = setup();
  const a = ticket(), b = ticket(), c = ticket();
  const first = await sync(a, game('A', 'one', 'A', ['A']));
  roll = .99;
  const second = await sync(b, game('B', 'two', 'B', ['B']));
  assert.notEqual(first.members[0].color, second.members[0].color);
  const room = await sync(c, game('C', 'one', 'A', ['C', 'A']));
  assert.equal(room.members[1].color, first.members[0].color);
  assert.notEqual(room.members[0].color, first.members[0].color);
  await sync(c, game('C'));
  assert.equal((await sync(a, game('A', 'one', 'A', ['A']))).members[0].color, first.members[0].color);
});

test('从名单消失的客户端停止共享，其他成员随后重新观察到其座位时能恢复同步', async () => {
  const { sync, ok } = setup();
  const a = ticket(), b = ticket(), together = uid => game(uid, 'L', 'A', ['A', 'B']);
  await sync(a, together('A'));
  await sync(b, together('B'));
  await sync(a, game('A', 'L', 'A', ['A']));
  assert.ok(!(await sync(b, together('B'))).roomId);
  const recovered = await sync(a, together('A'));
  const joined = await sync(b, together('B'));
  assert.equal(joined.roomId, recovered.roomId);
  assert.equal(joined.members.filter(s => s.hasPlugin).length, 2);
  await ok(a, 'leave');
  assert.equal((await sync(b, together('B'))).members.filter(s => s.hasPlugin).length, 1);
});

test('迟到的发布或搜索命令按凭证序号丢弃，退出优先于所有同步', async () => {
  const { sync, ok } = setup();
  const a = ticket(), b = ticket(), lobby = game('A', 'L', 'A', ['A']);
  const { roomId } = await sync(a, lobby, { sequence: 1 });
  await ok(a, 'recruit', { roomId, invitation: '旧邀请', sequence: 2 });
  await ok(a, 'stop-recruit', { roomId, sequence: 3 });
  assert.equal((await ok(a, 'recruit', { roomId, invitation: '旧邀请', sequence: 2 })).recruiting, false);
  await sync(b, game('B'), { sequence: 1 });
  await ok(b, 'search', { sequence: 2 });
  await ok(b, 'cancel-search', { sequence: 3 });
  assert.equal((await ok(b, 'search', { sequence: 2 })).phase, 'idle');
  await ok(a, 'leave', { sequence: 1 });
  assert.equal((await sync(a, lobby, { sequence: 4 })).phase, 'ended');
});

test('同游戏房号的访客画笔只分发给实际插件成员，屏蔽、离开、缓存与十秒过期按房间隔离', async () => {
  const { sync, ok, live, advance } = setup();
  const a = ticket(), b = ticket(), c = ticket();
  const battle = uid => game(uid, 'game-1', '', ['A', 'B', 'C'], 'playing');
  const room = await sync(a, battle('A'));
  const line = stroke(), body = { roomId: room.roomId, ...line };
  assert.equal((await live(a, 'stroke', body)).status, 409);
  const guest = await sync(b, battle('B'));
  await sync(c, game('C-other', 'game-2', '', ['C-other'], 'playing'));
  assert.equal((await live(a, 'stroke', { ...body, points: [{ x: 1.1, y: 0 }] })).status, 400);
  assert.equal((await live(a, 'stroke', { ...body, roomId: randomUUID() })).status, 409);
  await ok(a, 'stroke', body);
  await ok(a, 'stroke', body);
  assert.deepEqual((await sync(b, battle('B'))).strokes, [{ ...line, playerId: room.selfId, expiresAt: room.serverNow + 10_000 }]);
  assert.deepEqual((await sync(b, battle('B'), { knownStrokes: [line.id] })).strokes[0].points, []);
  assert.deepEqual((await ok(b, 'mute', { roomId: room.roomId, muted: [room.selfId] })).strokes, []);
  const spectatorId = room.members[2].id;
  const muted = await ok(a, 'mute', { roomId: room.roomId, muted: [spectatorId, guest.selfId] });
  assert.deepEqual(muted.muted, [guest.selfId]);
  assert.equal((await live(a, 'stroke', { ...body, id: randomUUID() })).status, 409);
  await ok(a, 'mute', { roomId: room.roomId, muted: [] });
  assert.deepEqual((await sync(c, game('C-other', 'game-2', '', ['C-other'], 'playing'))).strokes, []);
  advance(10_000);
  assert.deepEqual((await ok(a, 'stroke', body)).strokes, []);
  await ok(b, 'leave');
  assert.equal((await sync(b, battle('B'))).phase, 'ended');
  assert.deepEqual((await sync(a, battle('A'))).members.map(m => m.hasPlugin), [true, false, false]);
  assert.equal((await live(a, 'stroke', { ...body, id: randomUUID() })).status, 409);
});

test('开局改号只迁移有游戏名单佐证的队伍，原队伍其余成员保持独立', async () => {
  const { sync, ok } = setup();
  const a = ticket(), b = ticket();
  const old = uid => game(uid, 'L', 'B', ['A', 'B']);
  const previous = await sync(b, old('B'));
  await sync(a, old('A'));
  const different = await sync(a, game('A', 'G', '', ['A', 'X'], 'playing'));
  assert.notEqual(different.roomId, previous.roomId);
  const remaining = await sync(b, old('B'));
  assert.equal(remaining.roomId, previous.roomId);
  assert.equal(remaining.phase, 'lobby');
  assert.equal(remaining.members.filter(m => m.hasPlugin).length, 1);
  assert.equal((await ok(b, 'force-start', { roomId: remaining.roomId })).phase, 'playing');
  assert.equal((await sync(b, old('B'))).phase, 'playing');
});

test('对局已存在时合并晚到的大厅，保留该对局已有的笔迹和个人屏蔽', async () => {
  const { sync, ok } = setup();
  const a = ticket(), b = ticket(), c = ticket(), d = ticket();
  const initial = uid => game(uid, 'G', '', ['A', 'B'], 'playing');
  const full = uid => game(uid, 'G', '', ['A', 'B', 'C', 'D'], 'playing');
  const other = uid => game(uid, 'L', 'C', ['C', 'D']);
  const host = await sync(a, initial('A'));
  await sync(b, initial('B'));
  const line = stroke();
  await ok(a, 'stroke', { roomId: host.roomId, ...line });
  await ok(b, 'mute', { roomId: host.roomId, muted: [host.selfId] });
  await sync(c, other('C'));
  await sync(d, other('D'));
  const merged = await sync(d, full('D'));
  assert.equal(merged.roomId, host.roomId);
  assert.equal(merged.members.filter(m => m.hasPlugin).length, 4);
  assert.deepEqual(merged.strokes.map(s => s.id), [line.id]);
  const muted = await sync(b, full('B'));
  assert.deepEqual(muted.strokes, []);
  assert.deepEqual(muted.muted, [host.selfId]);
});

test('新观察者上报的对局名单替代合并前两个房间的旧座位总和', async () => {
  const { sync } = setup();
  const a = ticket(), e = ticket(), x = ticket();
  await sync(a, game('A', 'G', '', ['A', 'B', 'C', 'X'], 'playing'));
  await sync(e, game('E', 'L', 'E', ['E', 'F']));
  const final = await sync(x, game('X', 'G', '', ['X', 'E', 'F'], 'playing'));
  assert.deepEqual(final.members.map(m => m.nickname), ['博士X', '博士E', '博士F']);
  assert.equal(final.members.filter(m => m.hasPlugin).length, 2);
});

test('测试强制开始后扩大对局名单，旧测试笔迹不会传给新游戏队友，重发不能让它再次出现', async () => {
  const { sync, ok } = setup();
  const a = ticket(), b = ticket(), c = ticket();
  const lobby = uid => game(uid, 'L', 'A', ['A', 'B']);
  const battle = uid => game(uid, 'G', '', ['A', 'B', 'C'], 'playing');
  const { roomId } = await sync(a, lobby('A'));
  await sync(b, lobby('B'));
  await ok(a, 'force-start', { roomId });
  const line = stroke();
  await ok(a, 'stroke', { roomId, ...line });
  await sync(b, battle('B'));
  const guest = await sync(c, battle('C'));
  assert.equal(guest.roomId, roomId);
  assert.deepEqual(guest.strokes, []);
  assert.deepEqual((await ok(a, 'stroke', { roomId, ...line })).strokes, []);
});

test('凭证、房间与上报代次各自隔离，旧版匹配继续独立工作，停用凭证或超时房间无法复活', async () => {
  const { request, live, sync, ok, advance } = setup();
  const a = ticket(), b = ticket(), lobby = game('A', 'L', 'A', ['A']);
  assert.equal((await live('invalid', 'sync', { game: lobby })).status, 401);
  const queued = await request(a, 'join', { nickname: '旧版', strategy: tactics[0], tactic: '随便' });
  assert.equal(queued.data.phase, 'queued');
  assert.equal((await live(a, 'rooms')).status, 404);
  assert.equal((await live(a, 'sync', { game: { ...lobby, members: [seat('X')] }, strategies: [] })).status, 400);
  const room = await sync(a, lobby, { sequence: 1 });
  assert.equal((await sync(a, game('A', 'L', 'A', ['A'], 'matching'), { sequence: 3 })).phase, 'matching');
  assert.equal((await sync(a, lobby, { sequence: 2 })).phase, 'matching');
  assert.equal((await live(a, 'sync', { game: game('different'), strategies: [], sequence: 4 })).status, 409);
  assert.equal((await live(a, 'sync', { game: lobby, strategies: ['非法策略'] })).status, 400);
  await ok(b, 'leave');
  assert.equal((await sync(b, game('B'))).phase, 'ended');
  advance(90_000);
  assert.equal((await sync(a, lobby)).phase, 'ended');
  assert.notEqual((await sync(ticket(), lobby)).roomId, room.roomId);
});

const shout = (roomId, sequence = 1) => ({ roomId, sequence, template: '当心□！', word: '折光' });

test('快捷喊话由服务端组合白名单，不接受自由输入，只能发送给同一对局的插件队友', async () => {
  const { sync, live, ok } = setup({ supportsShouts: true });
  const a = ticket(), b = ticket(), c = ticket();
  const lobby = uid => game(uid, 'L', 'A', ['A', 'B']);
  const battle = uid => game(uid, 'G', '', ['A', 'B'], 'playing');
  const other = game('C', 'G2', '', ['C'], 'playing');
  const { roomId } = await sync(a, lobby('A'));
  assert.equal((await live(a, 'shout', shout(roomId))).status, 409);
  await sync(a, battle('A'));
  assert.equal((await live(a, 'shout', shout(roomId))).status, 409);
  await sync(b, battle('B'));
  const outsider = await sync(c, other);
  assert.equal((await live(a, 'shout', shout(outsider.roomId))).status, 409);
  for (const bad of [{ text: '你好' }, { template: '自由□' }, { word: '不在词库的自由文本' }, { word: ['折光'] }, { sequence: undefined }]) {
    assert.equal((await live(a, 'shout', { ...shout(roomId), ...bad })).status, 400);
  }
  const sent = await ok(a, 'shout', shout(roomId));
  assert.equal(sent.shouts.length, 1);
  const [{ id }] = sent.shouts;
  assert.match(id, /^[0-9a-f-]{36}$/);
  assert.deepEqual(sent.shouts, [{ id, playerId: sent.selfId, text: '当心折光！', sentAt: sent.serverNow }]);
  assert.deepEqual((await sync(b, battle('B'))).shouts, sent.shouts);
  assert.deepEqual((await sync(c, other)).shouts, []);
  assert.deepEqual((await ok(a, 'leave')).shouts, []);
  assert.deepEqual((await sync(b, battle('B'))).shouts, []);
});

test('快捷喊话对每个发送者独立限流：1999 毫秒拒发，2000 毫秒可发，迟到重试不复制，按时序保留最近八条', async () => {
  const { sync, ok, live, advance } = setup({ supportsShouts: true });
  const a = ticket(), b = ticket(), battle = uid => game(uid, 'G', '', ['A', 'B'], 'playing');
  const { roomId } = await sync(a, battle('A'));
  await sync(b, battle('B'));
  const first = await ok(a, 'shout', shout(roomId));
  assert.deepEqual((await ok(a, 'shout', shout(roomId))).shouts, first.shouts);
  const reply = await ok(b, 'shout', shout(roomId));
  assert.deepEqual(reply.shouts.map(x => x.playerId), [first.selfId, reply.selfId]);
  const ids = reply.shouts.map(x => x.id);
  advance(1999);
  assert.equal((await live(a, 'shout', shout(roomId, 2))).status, 429);
  advance(1);
  for (let seq = 2; seq <= 10; seq++) {
    const updated = await ok(a, 'shout', shout(roomId, seq));
    ids.push(updated.shouts.at(-1).id);
    advance(2000);
  }
  assert.equal(new Set(ids).size, 11);
  assert.deepEqual((await sync(b, battle('B'))).shouts.map(x => x.id), ids.slice(-8));
});

test('快捷喊话尊重双方屏蔽：第三位队友仍能接收，屏蔽期间的消息解禁后不补发', async () => {
  const { sync, ok, live, advance } = setup({ supportsShouts: true });
  const a = ticket(), b = ticket(), c = ticket(), battle = uid => game(uid, 'G', '', ['A', 'B', 'C'], 'playing');
  const host = await sync(a, battle('A')), second = await sync(b, battle('B'));
  const { roomId } = host;
  await ok(b, 'mute', { roomId, muted: [host.selfId] });
  assert.equal((await live(a, 'shout', shout(roomId))).status, 409);
  assert.equal((await live(b, 'shout', shout(roomId))).status, 409);
  await sync(c, battle('C'));
  const sent = await ok(a, 'shout', shout(roomId));
  assert.deepEqual((await sync(c, battle('C'))).shouts, sent.shouts);
  assert.deepEqual((await ok(b, 'mute', { roomId, muted: [] })).shouts, []);
  advance(2000);
  const secondSend = await ok(a, 'shout', shout(roomId, 2));
  assert.deepEqual((await sync(b, battle('B'))).shouts, secondSend.shouts.slice(-1));
  assert.deepEqual((await ok(b, 'mute', { roomId, muted: [host.selfId] })).shouts, []);
  assert.deepEqual((await ok(a, 'mute', { roomId, muted: [second.selfId] })).shouts, secondSend.shouts);
});

test('强制测试房间的喊话不会随合房进入真实对局，也不能通过重发旧序号重新显示', async () => {
  const { sync, ok, live, advance } = setup({ supportsShouts: true });
  const a = ticket(), b = ticket(), c = ticket(), d = ticket();
  const left = uid => game(uid, 'L1', 'A', ['A', 'B']);
  const right = uid => game(uid, 'L2', 'C', ['C', 'D']);
  const battle = uid => game(uid, 'G', '', ['A', 'B', 'C', 'D'], 'playing');
  const first = await sync(a, left('A')), second = await sync(c, right('C'));
  await sync(b, left('B')); await sync(d, right('D'));
  await ok(a, 'force-start', { roomId: first.roomId });
  await ok(c, 'force-start', { roomId: second.roomId });
  assert.equal((await ok(a, 'shout', shout(first.roomId))).shouts.length, 1);
  assert.equal((await ok(c, 'shout', shout(second.roomId))).shouts.length, 1);
  const merged = await sync(b, battle('B'));
  assert.deepEqual(merged.shouts, []);
  advance(2000);
  assert.deepEqual((await ok(a, 'shout', shout(first.roomId))).shouts, []);
  // The reused room ID is not proof the sender left the test lobby. A delayed
  // command with a fresh sequence must not go to newly merged teammates.
  assert.equal((await live(a, 'shout', shout(first.roomId, 2))).status, 409);
  assert.equal((await live(b, 'shout', shout(merged.roomId, 1))).status, 409);
  assert.deepEqual((await sync(d, battle('D'))).shouts, []);
  await sync(c, battle('C'));
  const actual = await ok(c, 'shout', shout(merged.roomId, 2));
  assert.deepEqual((await sync(b, battle('B'))).shouts, actual.shouts);
  assert.deepEqual((await sync(a, battle('A'))).shouts, []);
});

test('新旧插件混部时只把明确支持喊话的队友计为收件人，老版仍能照常共享画笔', async () => {
  const { sync, ok, live } = setup();
  const a = ticket(), b = ticket(), c = ticket();
  const battle = uid => game(uid, 'G', '', ['A', 'B', 'C'], 'playing');
  const current = { supportsShouts: true };
  const host = await sync(a, battle('A'), current);
  const legacy = await sync(b, battle('B'));
  const { roomId } = host;
  const notDelivered = await live(a, 'shout', shout(roomId));
  assert.equal(notDelivered.status, 409);
  assert.match(notDelivered.data.error, /更新|升级/);
  assert.deepEqual(legacy.members.map(m => [m.hasPlugin, m.supportsShouts]), [[true,true], [true,false], [false,false]]);
  const oldSender = await live(b, 'shout', shout(roomId));
  assert.equal(oldSender.status, 409);
  const ink = stroke();
  await ok(b, 'stroke', { roomId, ...ink });
  assert.equal((await sync(a, battle('A'), current)).strokes[0].id, ink.id);
  // A string is not affirmative. A send while B is old must not surface when B upgrades.
  const third = await sync(c, battle('C'), current);
  assert.deepEqual(third.members.map(m => m.supportsShouts), [true,false,true]);
  const sent = await ok(a, 'shout', shout(roomId));
  assert.deepEqual((await sync(c, battle('C'), current)).shouts, sent.shouts);
  const oldPoll = await sync(b, battle('B'), { supportsShouts: 'true' });
  assert.equal(oldPoll.members.find(m => m.id === legacy.selfId).supportsShouts, false);
  assert.deepEqual(oldPoll.shouts, []);
  assert.deepEqual((await sync(b, battle('B'), current)).shouts, []);
  assert.equal((await ok(b, 'shout', shout(roomId))).shouts.at(-1).playerId, legacy.selfId);
  const downgraded = await sync(b, battle('B'));
  assert.equal(downgraded.members.find(m => m.id === legacy.selfId).supportsShouts, false);
  assert.deepEqual(downgraded.shouts, []);
});

test('房间昵称从真实座位取词，未安装插件的成员也可被点名，离房旧名不能重发', async () => {
  const { ok, live, sync, advance } = setup({ supportsShouts: true });
  const a = ticket(), b = ticket(), c = ticket();
  const battle = (uid, withGuests = true) => {
    const state = game(uid, 'R', 'A', withGuests ? ['A', 'B', 'G', 'H'] : ['A', 'B'], 'playing');
    const names = { A: '', B: '同名', G: '旅行$&博士', H: '同名' };
    return { ...state, nickname: uid === 'A' ? '房主甲' : names[uid],
      members: state.members.map(s => ({ ...s, nickname: names[s.uid] })) };
  };
  const first = await sync(a, battle('A'));
  assert.deepEqual(first.roomNicknames, ['房主甲', '同名', '旅行$&博士']);
  await sync(b, battle('B'));
  const other = await sync(c, game('Z', 'Q', 'Z', ['Z'], 'playing'));
  assert.deepEqual(other.roomNicknames, ['博士Z']);
  const send = (word, sequence) => live(a, 'shout', { ...shout(first.roomId, sequence), word, isRoomNickname: true });
  for (const word of ['博士Z', '随意输入', '折光', '旅行']) assert.equal((await send(word, 1)).status, 400);
  const sent = await ok(a, 'shout', { ...shout(first.roomId), word: '旅行$&博士', isRoomNickname: true });
  assert.equal(sent.shouts.at(-1).text, '当心旅行$&博士！');
  assert.equal((await sync(b, battle('B'))).shouts.at(-1).text, '当心旅行$&博士！');
  assert.equal((await send('旅行$&博士', 2)).status, 429);
  advance(2000);
  assert.deepEqual((await sync(a, battle('A', false))).roomNicknames, ['房主甲', '同名']);
  assert.equal((await send('旅行$&博士', 2)).status, 400);
  assert.equal((await send('房主甲', 2)).status, 200);
});
