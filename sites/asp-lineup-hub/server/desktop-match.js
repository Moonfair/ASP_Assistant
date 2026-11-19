const MATCH_ANY = '随便';
const MATCH_DEFENSE = '兜底';
const MATCH_ATTACK = '攻坚';
const MATCH_COLORS = ['#56B4E9', '#E69F00', '#CC79A7', '#48C790'];
const MATCH_ACTIONS = new Set(['join', 'state', 'confirm', 'invite', 'ready', 'leave', 'mute', 'stroke']);
const MATCH_QUEUE_LIMIT = 64;
const MATCH_ACTIVE_LIMIT = 1024;
const MATCH_IDLE_MS = 90_000;
const MATCH_STROKE_MS = 10_000;
const MATCH_REPLAY_MS = 24 * 60 * 60_000;

function matchText(value, limit, field) {
  const text = cleanText(value);
  if (!text || text.length > limit) throw new HttpError(400, field + '长度无效。');
  return text;
}

function matchProfile(body) {
  const nickname = matchText(body.nickname, 32, '昵称');
  const strategy = matchText(body.strategy, 64, '策略');
  const covenant = matchText(body.covenant ?? MATCH_ANY, 64, '盟约');
  if (!hasOwn(catalog.strategies, strategy)) throw new HttpError(400, '请选择目录中的策略。');
  if (covenant !== MATCH_ANY && !catalog.covenantTags.includes(covenant)) throw new HttpError(400, '请选择目录中的盟约。');
  const tactic = body.tactic ?? MATCH_ANY;
  if (![MATCH_ANY, MATCH_DEFENSE, MATCH_ATTACK].includes(tactic)) throw new HttpError(400, '战术无效。');
  return { nickname, strategy, covenant, tactic };
}

function preferenceOverlap(a, b) {
  return Number(a.strategy === b.strategy) +
    Number(a.covenant !== MATCH_ANY && a.covenant === b.covenant);
}

// A fresh strict quartet must contain the new arrival. A relaxed quartet always
// contains the oldest player. FIFO iteration breaks ties in both cases.
function findMatch(queue, anchor, relaxed) {
  const candidates = queue.filter(p => p !== anchor);
  if (candidates.length < 3) return null;
  const base = anchor.member;
  let best = null, bestScore = Infinity;
  for (let i = 0; i < candidates.length - 2; i++) {
    const a = candidates[i].member;
    const aOverlap = preferenceOverlap(base, a);
    if (!relaxed && aOverlap) continue;
    for (let j = i + 1; j < candidates.length - 1; j++) {
      const b = candidates[j].member;
      const abOverlap = aOverlap + preferenceOverlap(base, b) + preferenceOverlap(a, b);
      if (!relaxed && abOverlap) continue;
      const nDefense = Number(base.tactic === MATCH_DEFENSE) + Number(a.tactic === MATCH_DEFENSE) + Number(b.tactic === MATCH_DEFENSE);
      const nAttack = Number(base.tactic === MATCH_ATTACK) + Number(a.tactic === MATCH_ATTACK) + Number(b.tactic === MATCH_ATTACK);
      if (!relaxed && (nDefense > 2 || nAttack > 2)) continue;
      for (let k = j + 1; k < candidates.length; k++) {
        const c = candidates[k].member;
        const imbalance = Math.max(0, nDefense + Number(c.tactic === MATCH_DEFENSE) - 2,
          nAttack + Number(c.tactic === MATCH_ATTACK) - 2);
        // At most 12 overlapping pairs across both preferences.
        const score = 13 * imbalance + abOverlap + preferenceOverlap(base, c) + preferenceOverlap(a, c) + preferenceOverlap(b, c);
        if (score === 0) return [anchor, candidates[i], candidates[j], candidates[k]];
        if (relaxed && score < bestScore) {
          best = [anchor, candidates[i], candidates[j], candidates[k]];
          bestScore = score;
        }
      }
    }
  }
  return best;
}

function assignMatchRoles(players) {
  const colors = MATCH_COLORS.slice();
  let defense = players.filter(p => p.member.tactic === MATCH_DEFENSE).length;
  let attack = players.filter(p => p.member.tactic === MATCH_ATTACK).length;
  for (const { member } of players) {
    const index = Math.floor(Math.random() * colors.length);
    member.color = colors.splice(index, 1)[0];
    if (member.tactic !== MATCH_ANY) member.assignedTactic = member.tactic;
    else if (defense <= attack) { member.assignedTactic = MATCH_DEFENSE; defense++; }
    else { member.assignedTactic = MATCH_ATTACK; attack++; }
  }
}

function pruneMatchStrokes(room, now) {
  room.strokes = room.strokes.filter(s => s.expiresAt > now);
  // Keep recent IDs after the ink fades so retries do not bring it back.
  for (const [id, seen] of room.strokeIds) {
    if (seen.at + 120_000 > now) break;
    room.strokeIds.delete(id);
  }
}

function sendMatchStroke(player, body, now) {
  const room = player.room;
  if (room?.phase !== 'playing') throw new HttpError(409, '进入游戏后才可使用画笔。');
  const { id, points } = body;
  if (!validId(id) || !Array.isArray(points) || !points.length || points.length > 512 ||
      points.some(p => !p || !Number.isFinite(p.x) || !Number.isFinite(p.y) || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1)) {
    throw new HttpError(400, '笔迹数据无效。');
  }
  pruneMatchStrokes(room, now);
  const previous = room.strokeIds.get(id);
  if (previous) {
    if (previous.owner !== player.member.id) throw new HttpError(409, '笔迹编号重复。');
    return;
  }
  if (!room.players.some(p => p !== player && !player.muted.has(p.member.id))) {
    throw new HttpError(409, '没有可用的队友画笔。');
  }
  const mine = room.strokes.filter(s => s.playerId === player.member.id);
  if (mine.length >= 32 || mine.reduce((n, s) => n + s.points.length, points.length) > 4096) {
    throw new HttpError(429, '画笔过于频繁，请稍后再试。');
  }
  room.strokeIds.set(id, { owner: player.member.id, at: now });
  room.strokes.push({ id, playerId: player.member.id, points: points.map(({ x, y }) => ({ x, y })), expiresAt: now + MATCH_STROKE_MS });
}

export class DesktopMatchService {
  constructor(now = Date.now) {
    this.now = now;
    this.players = new Map();
    this.queue = [];
    this.rooms = new Set();
    this.ended = new Map();
    this.claimsByIp = new Map();
    this.claimCount = 0;
    this.claimWindowEnds = 0;
    this.nextSweep = 0;
  }

  countNewAttempt(ip, now) {
    if (now >= this.claimWindowEnds) {
      this.claimsByIp.clear();
      this.claimCount = 0;
      this.claimWindowEnds = now + 60_000;
    }
    const count = this.claimsByIp.get(ip) ?? 0;
    if (count >= 60 || this.claimCount >= 600) throw new HttpError(429, '匹配请求过多，请稍后再试。');
    this.claimsByIp.set(ip, count + 1);
    this.claimCount++;
  }

  rememberEnd(key, selfId, message, now) {
    this.ended.set(key, { selfId, message, at: now });
    if (this.ended.size > 65_536) this.ended.delete(this.ended.keys().next().value);
  }

  forget(player, message, now) {
    this.players.delete(player.key);
    this.rememberEnd(player.key, player.member.id, message, now);
  }

  closeRoom(room, message, now) {
    for (const participant of room.players) this.forget(participant, message, now);
    this.rooms.delete(room);
  }

  remove(player, message, now) {
    const room = player.room;
    if (room && room.phase !== 'playing') {
      this.closeRoom(room, message, now);
      return;
    }
    if (room) {
      const id = player.member.id;
      room.players = room.players.filter(p => p !== player);
      for (const p of room.players) p.muted.delete(id);
      room.strokes = room.strokes.filter(s => s.playerId !== id);
      if (room.captainId === id) room.captainId = undefined;
      if (!room.players.length) this.rooms.delete(room);
    } else this.queue = this.queue.filter(p => p !== player);
    this.forget(player, message, now);
  }

  createRoom(group, now) {
    const players = this.queue.filter(p => group.includes(p));
    this.queue = this.queue.filter(p => !group.includes(p));
    assignMatchRoles(players);
    const room = { id: crypto.randomUUID(), phase: 'confirming', deadline: now + 20_000, players, strokes: [], strokeIds: new Map() };
    for (const player of players) player.room = room;
    this.rooms.add(room);
  }

  admitWaiting(now) {
    while (this.queue.length >= 4 && this.queue[0].joined + 60_000 <= now) {
      this.createRoom(findMatch(this.queue, this.queue[0], true), now);
    }
  }

  maintain(now = this.now()) {
    if (now >= this.nextSweep) {
      this.nextSweep = now + 1000;
      for (const room of this.rooms) {
        if (room.deadline <= now) this.closeRoom(room, '确认超时，本次匹配已结束。', now);
        else pruneMatchStrokes(room, now);
      }
      for (const player of this.players.values()) {
        if (player.seen + MATCH_IDLE_MS <= now) this.remove(player, '玩家已掉线，本次匹配已结束。', now);
      }
      for (const [key, record] of this.ended) {
        if (record.at + MATCH_REPLAY_MS > now) break;
        this.ended.delete(key);
      }
    }
    this.admitWaiting(now);
  }

  enqueue(key, body, ip, now) {
    const info = matchProfile(body);
    if (this.queue.length >= MATCH_QUEUE_LIMIT || this.players.size >= MATCH_ACTIVE_LIMIT) {
      throw new HttpError(429, '匹配队列已满，请稍后再试。');
    }
    this.countNewAttempt(ip, now);
    const member = { id: crypto.randomUUID(), ...info, assignedTactic: info.tactic, color: '', confirmed: false, ready: false };
    const player = { key, member, joined: now, seen: now, room: null, muted: new Set() };
    this.players.set(key, player);
    this.queue.push(player);
    if (this.queue.length >= 4 && this.queue[0].joined + 60_000 <= now) this.admitWaiting(now);
    else {
      const group = findMatch(this.queue, player, false);
      if (group) this.createRoom(group, now);
    }
  }

  command(player, action, body, now) {
    const room = player.room, member = player.member;
    switch (action) {
      case 'join':
      case 'state': return;
      case 'leave':
        this.remove(player, '玩家已退出，本次匹配已结束。', now);
        return;
      case 'confirm':
        if (member.confirmed) return;
        if (room?.phase !== 'confirming') throw new HttpError(409, '当前无需确认。');
        member.confirmed = true;
        if (room.players.every(p => p.member.confirmed)) {
          room.phase = 'creating';
          room.deadline = undefined;
          room.captainId = room.players[Math.floor(Math.random() * room.players.length)].member.id;
        }
        return;
      case 'invite': {
        if (room?.captainId !== member.id) throw new HttpError(403, '只有队长可以提交房间邀请。');
        const invitation = matchText(body.invitation, 1024, '邀请信息');
        if (room.invitation === invitation) return;
        if (room.phase !== 'creating') throw new HttpError(409, '当前无法提交房间邀请。');
        room.invitation = invitation;
        member.ready = true;
        room.phase = 'joining';
        return;
      }
      case 'ready':
        if (member.ready) return;
        if (room?.phase !== 'joining') throw new HttpError(409, '请等待队长创建房间。');
        member.ready = true;
        if (room.players.every(p => p.member.ready)) room.phase = 'playing';
        return;
      case 'mute': {
        if (room?.phase !== 'playing') throw new HttpError(409, '进入游戏后才可屏蔽画笔。');
        if (!Array.isArray(body.muted) || body.muted.length > 3 || !body.muted.every(validId)) throw new HttpError(400, '画笔屏蔽列表无效。');
        const peers = room.players.filter(p => p !== player).map(p => p.member.id);
        player.muted = new Set(body.muted.filter(id => peers.includes(id)));
        return;
      }
      case 'stroke': sendMatchStroke(player, body, now); return;
    }
  }

  snapshot(key, known, now) {
    const player = this.players.get(key);
    if (!player) {
      const ended = this.ended.get(key);
      return { phase: 'ended', selfId: ended?.selfId ?? '', serverNow: now,
        message: ended?.message ?? '本次匹配已结束。', members: [], muted: [], strokes: [] };
    }
    const room = player.room;
    if (room) pruneMatchStrokes(room, now);
    const strokes = (room?.strokes ?? []).filter(s => !player.muted.has(s.playerId))
      .map(s => known?.has(s.id) ? { ...s, points: [] } : s);
    return { phase: room?.phase ?? 'queued', selfId: player.member.id, roomId: room?.id,
      serverNow: now, deadline: room?.deadline, captainId: room?.captainId, invitation: room?.invitation,
      members: room ? room.players.map(p => p.member) : [player.member], muted: [...player.muted], strokes };
  }

  execute(key, action, body, ip) {
    const now = this.now();
    this.maintain(now);
    let known;
    if (action === 'state' && body.knownStrokes !== undefined) {
      if (!Array.isArray(body.knownStrokes) || body.knownStrokes.length > 128 || !body.knownStrokes.every(validId)) {
        throw new HttpError(400, '已缓存笔迹列表无效。');
      }
      known = new Set(body.knownStrokes);
    }
    const player = this.players.get(key);
    if (player) {
      if (player.room?.deadline <= now) this.closeRoom(player.room, '确认超时，本次匹配已结束。', now);
      else if (player.seen + MATCH_IDLE_MS <= now) this.remove(player, '玩家已掉线，本次匹配已结束。', now);
      else {
        player.seen = now;
        this.command(player, action, body, now);
      }
    } else if (!this.ended.has(key)) {
      if (action === 'join') this.enqueue(key, body, ip, now);
      // Leave may arrive before an in-flight join for the same attempt.
      else if (action === 'leave') {
        this.countNewAttempt(ip, now);
        this.rememberEnd(key, '', '本次匹配已取消。', now);
      }
    }
    return this.snapshot(key, known, now);
  }
}

async function desktopMatchApi(request, action, env) {
  if (!MATCH_ACTIONS.has(action)) throw new HttpError(404, '找不到这个匹配操作。');
  if (request.method !== 'POST') throw new HttpError(405, '匹配接口需要使用 POST。');
  const ticket = /^Bearer ([0-9a-f]{64})$/i.exec(request.headers.get('authorization') ?? '')?.[1];
  if (!ticket) throw new HttpError(401, '匹配凭证无效。');
  if (!env.matchService) throw new HttpError(503, '匹配服务暂时不可用。');
  const body = await readBody(request, action === 'stroke' ? 32_768 : 8192);
  const key = await digest('desktop-match:' + ticket.toLowerCase());
  return reply(env.matchService.execute(key, action, body, env.clientIp ?? 'unknown'));
}
