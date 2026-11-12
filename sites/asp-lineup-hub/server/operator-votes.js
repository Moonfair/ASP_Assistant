const voteResultTtl = 60_000;
const voteResultsByDatabase = new WeakMap();

function cachedOperatorVotes(db, season, operator) {
  let cache = voteResultsByDatabase.get(db);
  if (!cache) voteResultsByDatabase.set(db, cache = new Map());
  const now = Date.now();
  const cached = cache.get(operator);
  if (cached && cached.expiresAt > now) return cached.results;

  // Only catalog entries reach this cache. Concurrent reads share one query.
  const results = (async () => {
    const { results: rows } = await db.prepare(
      'SELECT kind, choice, votes FROM operator_vote_totals WHERE season = ? AND operator = ?'
      + ' ORDER BY votes DESC, choice ASC'
    ).bind(season, operator).all();
    const slots = operatorVoteCatalog.operators[operator];
    return {
      skills: rows.filter(row => row.kind === 'skill' && slots.includes(Number(row.choice)))
        .map(({ choice, votes }) => ({ choice: Number(choice), votes })),
      equipment: rows.filter(row => row.kind === 'equipment' && hasOwn(catalog.equipment, row.choice))
        .slice(0, 5).map(({ choice, votes }) => ({ choice, votes }))
    };
  })();
  cache.set(operator, { results, expiresAt: now + voteResultTtl });
  results.catch(() => {
    if (cache.get(operator)?.results === results) cache.delete(operator);
  });
  return results;
}

async function operatorVotesApi(request, url, env) {
  const { DB: db } = env;
  const season = url.searchParams.get('season');
  const operator = url.searchParams.get('operator');
  if (season !== operatorVoteCatalog.season || !hasOwn(operatorVoteCatalog.operators, operator))
    throw new HttpError(404, '当前赛季没有这名干员的投票。');

  const mine = url.pathname.endsWith('/mine');
  if (!mine && request.method === 'GET') {
    const results = await cachedOperatorVotes(db, season, operator);
    return reply(results, 200, { 'Cache-Control': 'public, max-age=60' });
  }
  if (!(mine && request.method === 'GET') && !(!mine && request.method === 'POST'))
    throw new HttpError(405, '此接口不支持该请求方式。');

  const desktop = request.headers.has('authorization');
  const account = await requireSession(db, request, desktop);
  const voter = await digest('operator-ballot:' + account.id);
  if (mine) {
    const { results } = await db.prepare('SELECT kind FROM operator_ballots WHERE season = ? AND operator = ? AND voter = ?')
      .bind(season, operator, voter).all();
    return reply({
      skillVoted: results.some(row => row.kind === 'skill'),
      equipmentVoted: results.some(row => row.kind === 'equipment')
    });
  }

  const body = await readBody(request, 2048);
  let kind, choices;
  if (Object.keys(body).length === 1 && Number.isInteger(body.skill)
      && operatorVoteCatalog.operators[operator].includes(body.skill)) {
    kind = 'skill';
    choices = [body.skill];
  } else if (Object.keys(body).length === 1 && Array.isArray(body.equipment)
      && body.equipment.length > 0 && body.equipment.length <= 5
      && new Set(body.equipment).size === body.equipment.length
      && body.equipment.every(name => typeof name === 'string' && hasOwn(catalog.equipment, name))) {
    kind = 'equipment';
    choices = body.equipment;
  } else {
    throw new HttpError(400, '请选择一项可用技能，或 1–5 件不同的装备。');
  }

  const ballot = db.prepare('SELECT 1 FROM operator_ballots WHERE season = ? AND operator = ? AND voter = ? AND kind = ?')
    .bind(season, operator, voter, kind);
  if (await ballot.first()) return reply({ recorded: true });

  const now = Date.now();
  const limitMessage = '投票过于频繁，请稍后再试。';
  await takeAuthLimit(db, 'operator-vote:' + voter, now + 60_000, 20, now, limitMessage);
  const ipKey = await digest('operator-vote-ip:' + (env.clientIp ?? 'unknown'));
  await takeAuthLimit(db, 'operator-vote-ip:' + ipKey, now + 60_000, 120, now, limitMessage);
  const inserted = await db.prepare('INSERT INTO operator_ballots VALUES (?, ?, ?, ?, ?)'
    + ' ON CONFLICT DO NOTHING RETURNING kind').bind(season, operator, voter, kind, JSON.stringify(choices)).first();
  return reply({ recorded: true }, inserted ? 201 : 200);
}
