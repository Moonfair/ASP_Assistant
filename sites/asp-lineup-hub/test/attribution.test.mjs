import test from 'node:test';
import assert from 'node:assert/strict';
import worker from '../dist/server/index.js';
import { openDatabase } from '../scripts/local-db.mjs';

test('阵容列表为已有阵容提供可点击的攻略来源', async (t) => {
  const DB = openDatabase(':memory:');
  t.after(() => DB.close());
  const sources = [
    ['原攻略作者', 'https://game.skland.com/arknights/stronghold-guide/detail?guideId=2120430515119446229'],
    ['原视频作者', 'https://www.bilibili.com/video/BV1poWu6bENz/'],
    ['恶意来源', 'https://game.skland.com.evil.test/arknights/stronghold-guide/detail?guideId=1']
  ];
  const insert = DB.prepare('INSERT INTO lineups (id, created_at, data) VALUES (?, ?, ?)');
  for (const [i, [author, url]] of sources.entries()) {
    const id = '00000000-0000-0000-0000-00000000000' + i;
    const description = '阵容正文很长。'.repeat(40) + '\n\n' + ['\x60\x60\x60aspa-source', author, url, '\x60\x60\x60'].join('\n');
    await insert.bind(id, Date.now() - i, JSON.stringify({
      title: '来源回归测试', author: '发布账号署名', description, members: [], covenants: [], search: '来源回归测试', sample: false
    })).run();
  }
  const response = await worker.fetch(new Request('https://lineup.example.test/api/lineups?source=community'), { DB });
  assert.equal(response.status, 200);
  const { items } = await response.json();
  assert.deepEqual(items.map(item => item.attribution), [
    { author: sources[0][0], url: sources[0][1] },
    { author: sources[1][0], url: sources[1][1] },
    undefined
  ]);
  assert.ok(items.every(item => item.description.length === 160));
});
