const LIVE_MATCH_ACTIONS = new Set(['sync', 'recruit', 'stop-recruit', 'search', 'next', 'cancel-search',
  'leave', 'force-start', 'mute', 'stroke', 'shout']);
const LIVE_HOST_MS = 12_000;
const LIVE_RELAX_MS = 60_000;
const LIVE_UNKNOWN = '未知';
const LIVE_SHOUT_CD_MS = 2000;
const LIVE_SHOUT_HISTORY = 8;
const liveShoutTemplates = new Set(quickChatCatalog.templates);
const liveShoutWords = new Set(Object.values(quickChatCatalog.groups).flat());

function liveRoomNicknames(room) {
  if (room?.phase !== 'playing') return [];
  // Use actual names (including seats without a plugin), never the display fallback “未知”.
  return [...new Set(room.seats.map(s => s.nickname || room.players.find(p => p.uid === s.uid)?.game?.nickname).filter(Boolean))];
}

function sendLiveShout(player, body, now) {
  const room = player.room;
  // A forced test lobby can keep its room UUID after matchmaking merges it.
  // Require each participant's own observation before routing real-battle chat.
  const inGame = p => room.forced || (p.game?.phase === 'playing' && p.game.roomId === room.gameId);
  if (room.phase !== 'playing' || !inGame(player)) throw new HttpError(409, '进入对局后才可快捷喊话。');
  if (!player.supportsShouts) throw new HttpError(409, '当前客户端不支持快捷喊话，请更新助手。');
  if (body.sequence === undefined || hasOwn(body, 'text') || !liveShoutTemplates.has(body.template) ||
      (body.isRoomNickname === true ? !liveRoomNicknames(room).includes(body.word) : !liveShoutWords.has(body.word)))
    throw new HttpError(400, '请选择快捷喊话的预设句式和词语。');
  const peers = room.players.filter(p => p !== player && inGame(p) && p.seen + MATCH_IDLE_MS > now &&
    !p.muted.has(player.member.id) && !player.muted.has(p.member.id));
  if (!peers.length) throw new HttpError(409, '没有可以接收喊话的队友。');
  const recipients = peers.filter(p => p.supportsShouts);
  if (!recipients.length) throw new HttpError(409, '队友的助手不支持快捷喊话，请队友更新后再试。');
  if (player.lastShoutAt !== undefined && now - player.lastShoutAt < LIVE_SHOUT_CD_MS)
    throw new HttpError(429, '喊话冷却中，请等待两秒再试。');
  player.lastShoutAt = now;
  const message = { id: crypto.randomUUID(), playerId: player.member.id,
    text: body.template.replace('□', () => body.word), sentAt: now };
  // Bind the message to participants at send time: late joiners, new tickets
  // and players who unmute later must not receive someone else's backlog.
  const audience = new Set([player.member.id, ...recipients.map(p => p.member.id)]);
  room.shouts.push({ message, audience });
  if (room.shouts.length > LIVE_SHOUT_HISTORY) room.shouts.shift();
}

function liveText(value, limit, label) {
  if (value == null) return '';
  if (typeof value !== 'string' || value.length > limit || /[\u0000-\u001f\u007f]/.test(value))
    throw new HttpError(400, label + '无效。');
  return value.trim();
}

function liveGame(value) {
  if (!value || !['idle', 'lobby', 'matching', 'playing'].includes(value.phase)) throw new HttpError(400, '游戏状态无效。');
  const { phase } = value;
  const selfUid = liveText(value.selfUid, 128, '玩家标识');
  const nickname = liveText(value.nickname, 64, '昵称');
  const roomId = liveText(value.roomId, 128, '游戏房间号');
  const hostUid = liveText(value.hostUid, 128, '房主标识');
  const roster = value.members ?? [];
  if (!Array.isArray(roster) || roster.length > MATCH_TEAM_SIZE) throw new HttpError(400, '游戏成员列表无效。');
  const members = roster.map(seat => {
    if (!seat || (seat.ready != null && typeof seat.ready !== 'boolean')) throw new HttpError(400, '游戏座位无效。');
    const uid = liveText(seat.uid, 128, '成员标识');
    if (!uid) throw new HttpError(400, '缺少成员标识。');
    return { uid, nickname: liveText(seat.nickname, 64, '昵称'), ready: seat.ready === true };
  });
  const uids = new Set(members.map(m => m.uid));
  const solo = phase === 'idle' || (phase === 'matching' && !roomId);
  if (uids.size !== members.length || (solo ? roomId || members.length || hostUid : !roomId || !uids.has(selfUid)))
    throw new HttpError(400, '游戏房间和成员信息不一致。');
  return { phase, selfUid, nickname, roomId, hostUid: uids.has(hostUid) ? hostUid : '', members };
}

function livePreferences(body) {
  const strategies = matchChoices(body.strategies ?? [], '策略', 0, name => hasOwn(catalog.strategies, name));
  const covenants = matchChoices(body.covenants ?? [], '盟约', 0, name => catalog.covenantTags.includes(name));
  const tactic = body.tactic ?? MATCH_ANY;
  if (![MATCH_ANY, MATCH_DEFENSE, MATCH_ATTACK].includes(tactic)) throw new HttpError(400, '战术无效。');
  const configured = strategies.length > 0 || covenants.length > 0 || tactic !== MATCH_ANY;
  return { strategies, covenants, strategy: strategies.join('、') || LIVE_UNKNOWN,
    covenant: covenants.join('、') || (configured ? MATCH_ANY : LIVE_UNKNOWN), tactic: configured ? tactic : LIVE_UNKNOWN };
}

function liveIds(value, max, label) {
  if (!Array.isArray(value) || value.length > max || !value.every(validId)) throw new HttpError(400, label + '无效。');
  return new Set(value);
}

function liveMembers(room) {
  const members = room.seats.map(seat => {
    const player = room.players.find(p => p.uid === seat.uid);
    const profile = player?.profile;
    return { id: seat.id, nickname: seat.nickname || player?.game?.nickname || LIVE_UNKNOWN,
      strategy: profile?.strategy ?? LIVE_UNKNOWN, covenant: profile?.covenant ?? LIVE_UNKNOWN,
      strategies: profile?.strategies ?? [], covenants: profile?.covenants ?? [],
      tactic: profile?.tactic ?? LIVE_UNKNOWN, assignedTactic: profile?.tactic ?? LIVE_UNKNOWN,
      color: seat.color, confirmed: true, ready: seat.ready, hasPlugin: !!player, supportsShouts: player?.supportsShouts === true };
  });
  const known = members.filter(m => m.tactic !== LIVE_UNKNOWN);
  assignMatchTactics(known.map(member => ({ member })));
  return members;
}

// Compare the projected room, including all choices of every known participant.
// Wildcards, unknown players and vacant seats can still fill either role.
function liveScore(room, seeker) {
  const profiles = [...room.players.map(p => p.profile), seeker.profile];
  const defense = profiles.filter(p => p.tactic === MATCH_DEFENSE).length;
  const attack = profiles.filter(p => p.tactic === MATCH_ATTACK).length;
  let overlap = 0;
  for (let i = 0; i < profiles.length; i++) {
    for (let j = 0; j < i; j++) overlap += preferenceOverlap(profiles[i], profiles[j]);
  }
  const size = room.seats.length + 1;
  return [Math.max(0, defense - 2, attack - 2), overlap, -size,
    Math.max(0, Math.abs(defense - attack) - (size - defense - attack)), room.listing.at];
}

function betterLiveScore(left, right) {
  if (!right) return true;
  for (let i = 0; i < left.length; i++) if (left[i] !== right[i]) return left[i] < right[i];
  return false;
}

function canPublishLiveRoom(room, player, now) {
  if (!player || player.room !== room || room.phase !== 'lobby' || room.seats.length >= MATCH_TEAM_SIZE ||
      !room.hostUid || room.hostUid !== player.uid || player.seen + LIVE_HOST_MS <= now ||
      player.game?.roomId !== room.gameId || player.game.hostUid !== player.uid || player.game.phase !== 'lobby') return false;
  return room.players.every(p => p.seen + LIVE_HOST_MS <= now ||
    (p.game?.roomId === room.gameId && p.game.phase === 'lobby' && p.game.hostUid === room.hostUid && p.game.members.length < MATCH_TEAM_SIZE));
}

export class LiveMatchService extends MatchTicketLedger {
  constructor(now = Date.now) {
    super();
    this.now = now;
    this.players = new Map();
    this.byUid = new Map();
    this.lobbies = new Map();
    this.battles = new Map();
    this.priorLobbies = new Map();
    this.listed = new Set();
    this.nextSweep = 0;
  }

  unlist(room) {
    room.listing = null;
    this.listed.delete(room);
  }

  checkListing(room, now) {
    if (room?.listing && !canPublishLiveRoom(room, room.listing.host, now)) this.unlist(room);
    return !!room?.listing;
  }

  removeRoom(room) {
    this.unlist(room);
    if (this.lobbies.get(room.gameId) === room) this.lobbies.delete(room.gameId);
    if (this.battles.get(room.gameId) === room) this.battles.delete(room.gameId);
    for (const id of room.origins) if (this.priorLobbies.get(id) === room) this.priorLobbies.delete(id);
  }

  detach(player, leftGame = false) {
    const room = player.room;
    if (!room) return;
    if (room.listing?.host === player) this.unlist(room);
    if (room.phase === 'playing' && !room.forced) player.leftBattle = room.gameId;
    room.players = room.players.filter(p => p !== player);
    for (const p of room.players) p.muted.delete(player.member.id);
    room.strokes = room.strokes.filter(s => s.playerId !== player.member.id);
    room.shouts = room.shouts.filter(s => s.message.playerId !== player.member.id);
    for (const s of room.shouts) s.audience.delete(player.member.id);
    if (leftGame) {
      room.seats = room.seats.filter(s => s.uid !== player.uid);
      if (room.hostUid === player.uid) room.hostUid = '';
    }
    player.room = null;
    player.muted.clear();
    if (!room.players.length) this.removeRoom(room);
  }

  retire(player, now) {
    this.detach(player);
    if (this.byUid.get(player.uid) === player) this.byUid.delete(player.uid);
    this.players.delete(player.key);
    this.rememberEnd(player.key, player.member.id, '本次房间同步已结束。', now);
  }

  maintain(now = this.now()) {
    if (now < this.nextSweep) return;
    this.nextSweep = now + 1000;
    for (const p of this.players.values()) if (p.seen + MATCH_IDLE_MS <= now) this.retire(p, now);
    for (const room of this.listed) this.checkListing(room, now);
    for (const room of this.battles.values()) pruneMatchStrokes(room, now);
    // A manually started test can still be observing the physical lobby.
    for (const room of this.lobbies.values()) if (room.forced) pruneMatchStrokes(room, now);
    this.pruneEnded(now);
  }

  createRoom(game) {
    const room = { id: crypto.randomUUID(), gameId: game.roomId, phase: game.phase, hostUid: '', forced: false,
      seats: [], players: [], listing: null, origins: new Set(), strokes: [], strokeIds: new Map(), shouts: [] };
    (game.phase === 'playing' ? this.battles : this.lobbies).set(room.gameId, room);
    return room;
  }

  // A battle can combine several matchmaking lobbies. Only carry a whole lobby
  // forward when the battle actually lists all its seats. An unrelated teammate
  // left behind must keep their own room and cannot receive this battle's ink.
  battleRoom(game, reporter) {
    const ids = new Set(game.members.map(m => m.uid));
    const sources = new Set();
    for (const uid of ids) {
      const source = this.byUid.get(uid)?.room;
      if (source && this.lobbies.get(source.gameId) === source && source.seats.every(s => ids.has(s.uid)) &&
          !source.players.some(p => p.leftBattle === game.roomId)) sources.add(source);
    }
    let room = this.battles.get(game.roomId);
    if (!room) {
      room = sources.has(reporter.room) ? reporter.room : sources.values().next().value;
      if (!room) return { room: this.createRoom(game), merged: false };
    }
    // A forced test has a lobby audience. Discard its visible ink when the actual
    // game adds seats, but keep the replay IDs and any existing real-game ink.
    if (room.forced && game.members.some(m => !room.seats.some(s => s.uid === m.uid))) room.strokes = [];
    if (room.forced) room.shouts = []; // A test lobby is not the real battle's chat.
    for (const source of sources) {
      this.unlist(source);
      const oldId = source.gameId;
      this.lobbies.delete(oldId);
      this.priorLobbies.set(oldId, room);
      room.origins.add(oldId);
      if (room.origins.size > MATCH_TEAM_SIZE) {
        const oldest = room.origins.values().next().value;
        room.origins.delete(oldest);
        if (this.priorLobbies.get(oldest) === room) this.priorLobbies.delete(oldest);
      }
      if (source === room) continue;
      for (const p of source.players) { p.room = room; room.players.push(p); }
      room.seats.push(...source.seats.filter(s => !room.seats.some(t => t.uid === s.uid)));
      source.players = [];
    }
    room.gameId = game.roomId;
    room.phase = 'playing';
    room.forced = false;
    this.battles.set(room.gameId, room);
    return { room, merged: sources.size > 0 };
  }

  updateSeats(room, game) {
    const prior = new Map(room.seats.map(s => [s.uid, s]));
    // The destination's seats come first in a merge. Reserve their existing
    // colors before drawing from the remaining palette for arrivals/collisions.
    const occupied = new Map();
    const palette = new Set(MATCH_COLORS);
    for (const seat of room.seats) {
      if (game.members.some(m => m.uid === seat.uid) && palette.delete(seat.color)) occupied.set(seat.uid, seat.color);
    }
    const available = [...palette];
    const seats = game.members.map(raw => {
      const old = prior.get(raw.uid);
      const player = room.players.find(p => p.uid === raw.uid);
      const color = occupied.get(raw.uid) ?? available.splice(Math.floor(Math.random() * available.length), 1)[0];
      return { ...raw, nickname: raw.nickname || old?.nickname || '', color,
        id: player?.member.id ?? old?.id ?? crypto.randomUUID() };
    });
    room.seats = seats;
    for (const p of [...room.players]) if (!seats.some(s => s.uid === p.uid)) this.detach(p);
    room.hostUid = game.hostUid;
  }

  observe(player, game, now) {
    const previous = player.game;
    const signature = JSON.stringify(game);
    if (!game.roomId) {
      this.detach(player, true);
      if (game.phase === 'matching') player.search = null;
      player.game = game;
      player.signature = signature;
      player.seen = now;
      return;
    }
    player.search = null;
    if (game.phase !== 'playing') {
      const awaitingBattle = player.room?.phase === 'playing' && !player.room.forced && previous?.phase !== 'playing' &&
        previous?.roomId === game.roomId;
      if (awaitingBattle) return;
      const battle = this.priorLobbies.get(game.roomId);
      if (battle?.seats.some(s => s.uid === game.selfUid) && player.leftBattle !== battle.gameId &&
          !(player.room === battle && previous?.phase === 'playing' && previous.roomId === battle.gameId)) return;
    }
    const changed = signature !== player.signature;
    // Another member may already have observed this player's departure. A retry
    // of their identical old report cannot insert them into that room again.
    if (!changed && !player.room) {
      const current = (game.phase === 'playing' ? this.battles : this.lobbies).get(game.roomId);
      if (!current?.seats.some(s => s.uid === player.uid)) return;
    }
    let room = player.room;
    let merged = false;
    if (!room || changed) {
      if (game.phase === 'playing') ({ room, merged } = this.battleRoom(game, player));
      else room = this.lobbies.get(game.roomId) ?? this.createRoom(game);
    }
    const attached = player.room !== room;
    if (attached) {
      this.detach(player, true);
      const seat = room.seats.find(s => s.uid === player.uid);
      if (seat) player.member.id = seat.id;
      player.room = room;
      room.players.push(player);
    }
    player.game = game;
    player.signature = signature;
    player.seen = now;
    if (game.phase === 'playing') player.leftBattle = '';
    if (game.phase === 'matching' && !room.forced) room.phase = 'matching';
    if (room.phase === 'matching' && game.phase === 'lobby' &&
        room.players.every(p => p.game?.phase === 'lobby' || p.seen + LIVE_HOST_MS <= now)) room.phase = 'lobby';
    // A newly connected client can still see an earlier, smaller version of the
    // same roster. Keep the seats already seen until an existing observer reports
    // a change. Identical polling reports only renew presence.
    const earlierSubset = attached && !merged && room.seats.length > game.members.length &&
      game.members.every(m => room.seats.some(s => s.uid === m.uid));
    if ((changed || attached) && !earlierSubset) this.updateSeats(room, game);
    this.checkListing(room, now);
  }

  findOffer(player, now) {
    const search = player.search;
    const score = room => {
      if (!this.checkListing(room, now) || search.skipped.has(room) || room.seats.some(s => s.uid === player.uid)) return null;
      const rank = liveScore(room, player);
      const relaxed = Math.min(search.started, room.listing.at) + LIVE_RELAX_MS <= now;
      return relaxed || (rank[0] === 0 && rank[1] === 0) ? rank : null;
    };
    if (search.offer && score(search.offer)) return search.offer;
    if (search.offer) search.skipped.add(search.offer);
    search.offer = null;
    let best = null;
    for (const room of this.listed) {
      const rank = score(room);
      if (rank && betterLiveScore(rank, best)) { best = rank; search.offer = room; }
    }
    return search.offer;
  }

  command(player, action, body, now) {
    if (action === 'search') {
      if (player.room || player.game?.phase !== 'idle') throw new HttpError(409, '请先退出游戏房间再搜索。');
      player.search ??= { started: now, offer: null, skipped: new WeakSet() };
      return;
    }
    if (action === 'cancel-search') { player.search = null; return; }
    if (action === 'next') {
      if (!validId(body.roomId)) throw new HttpError(400, '推荐房间标识无效。');
      if (player.search?.offer?.id === body.roomId) {
        player.search.skipped.add(player.search.offer);
        player.search.offer = null;
      }
      return;
    }
    const room = player.room;
    if (!room || room.id !== body.roomId) throw new HttpError(409, '游戏房间已变化，请刷新。');
    if (['recruit', 'stop-recruit', 'force-start'].includes(action) && (!room.hostUid || room.hostUid !== player.uid ||
        player.game?.hostUid !== player.uid)) throw new HttpError(403, '只有游戏房主可以执行此操作。');
    switch (action) {
      case 'recruit': {
        const invitation = matchText(body.invitation, 1024, '房间邀请').replace(/\r\n?/g, '\n');
        if (!canPublishLiveRoom(room, player, now)) throw new HttpError(409, '当前房间无法招募，请确认大厅还有空位。');
        if (!room.listing || room.listing.invitation !== invitation)
          room.listing = { invitation, host: player, at: room.listing?.at ?? now };
        this.listed.add(room);
        break;
      }
      case 'stop-recruit': this.unlist(room); break;
      case 'force-start':
        if (room.phase === 'playing') break;
        if (room.phase !== 'lobby' || player.game.phase !== 'lobby' || player.seen + LIVE_HOST_MS <= now)
          throw new HttpError(409, '当前游戏状态无法强制开始。');
        this.unlist(room);
        room.forced = true;
        room.phase = 'playing';
        break;
      case 'mute': {
        if (room.phase !== 'playing') throw new HttpError(409, '进入游戏后才可屏蔽画笔。');
        const ids = liveIds(body.muted, MATCH_TEAM_SIZE - 1, '画笔屏蔽列表');
        player.muted = new Set(room.players.filter(p => p !== player && ids.has(p.member.id)).map(p => p.member.id));
        break;
      }
      case 'stroke': sendMatchStroke(player, body, now); break;
      case 'shout': sendLiveShout(player, body, now); break;
    }
  }

  snapshot(player, now, known) {
    const room = player.room ?? (player.search && this.findOffer(player, now));
    const offer = !!room && !player.room;
    const captain = room?.seats.find(s => s.uid === room.hostUid)?.id;
    const recruiting = this.checkListing(room, now);
    if (player.room) pruneMatchStrokes(room, now);
    const strokes = (player.room?.strokes ?? []).filter(s => !player.muted.has(s.playerId));
    const shouts = (player.supportsShouts && player.room ? player.room.shouts : []).filter(({ message, audience }) =>
      audience.has(player.member.id) && !player.muted.has(message.playerId) &&
      !room.players.find(p => p.member.id === message.playerId)?.muted.has(player.member.id)).map(s => s.message);
    const outside = player.search ? 'searching' : player.game?.phase === 'matching' && !player.game.roomId ? 'matching' : 'idle';
    return { phase: offer ? 'offer' : room?.phase ?? outside, selfId: player.member.id,
      roomId: room?.id ?? null, gameRoomId: room?.gameId ?? null, captainId: captain ?? null, recruiting,
      canForceStart: !offer && room?.phase === 'lobby' && captain === player.member.id && player.game?.hostUid === player.uid &&
        player.seen + LIVE_HOST_MS > now,
      invitation: room?.listing?.invitation ?? null, members: room ? liveMembers(room) : [], muted: [...player.muted],
      roomNicknames: liveRoomNicknames(player.room),
      strokes: strokes.map(s => known?.has(s.id) ? { ...s, points: [] } : s), shouts, serverNow: now };
  }

  execute(key, action, body, ip) {
    const now = this.now();
    this.maintain(now);
    let player = this.players.get(key);
    if (player && player.seen + MATCH_IDLE_MS <= now) { this.retire(player, now); player = null; }
    if (action === 'leave') {
      if (player) this.retire(player, now);
      else if (!this.ended.has(key)) {
        this.countNewAttempt(ip, now);
        this.rememberEnd(key, '', '本次房间同步已取消。', now);
      }
      player = null;
    }
    if (this.ended.has(key) || (!player && action !== 'sync')) {
      const end = this.ended.get(key);
      return { phase: 'ended', selfId: end?.selfId ?? '', message: end?.message ?? '请重新同步游戏。', serverNow: now,
        roomId: null, gameRoomId: null, captainId: null, invitation: null,
        recruiting: false, canForceStart: false, members: [], muted: [], strokes: [], shouts: [] };
    }
    this.checkListing(player?.room, now);
    const sequence = body.sequence;
    if (sequence !== undefined && (!Number.isSafeInteger(sequence) || sequence < 0)) throw new HttpError(400, '同步序号无效。');
    const known = action === 'sync' && body.knownStrokes !== undefined ? liveIds(body.knownStrokes, 128, '已缓存笔迹列表') : null;
    if (player && sequence !== undefined && sequence <= player.sequence) return this.snapshot(player, now, known);
    if (action === 'sync') {
      const game = liveGame(body.game);
      const profile = livePreferences(body);
      const owner = this.byUid.get(game.selfUid);
      if (owner && owner !== player && owner.seen + MATCH_IDLE_MS <= now) this.retire(owner, now);
      if (game.selfUid && ((player?.uid && player.uid !== game.selfUid) ||
          (this.byUid.has(game.selfUid) && this.byUid.get(game.selfUid) !== player))) throw new HttpError(409, '游戏身份已变化或已在使用中。');
      if (!player) {
        if (this.players.size >= MATCH_ACTIVE_LIMIT) throw new HttpError(429, '正在组队的玩家过多，请稍后再试。');
        this.countNewAttempt(ip, now);
        player = { key, member: { id: crypto.randomUUID() }, uid: '', room: null, muted: new Set(), sequence: -1, seen: now };
        this.players.set(key, player);
      }
      if (game.selfUid) { player.uid = game.selfUid; this.byUid.set(player.uid, player); }
      if (player.search && JSON.stringify(player.profile) !== JSON.stringify(profile)) player.search.offer = null;
      player.profile = profile;
      player.supportsShouts = body.supportsShouts === true;
      this.observe(player, game, now);
    } else this.command(player, action, body, now);
    if (sequence !== undefined) player.sequence = sequence;
    return this.snapshot(player, now, known);
  }
}
