import test from 'node:test';
import assert from 'node:assert/strict';
import { openDatabase } from '../scripts/local-db.mjs';
import worker from '../dist/server/index.js';

const zip = Buffer.from('UEsDBBQAAAAIACp4bF1Do3mnGgAAABgAAAATAAAAYXNwYS0yMDI2LTExLTEyLmxvZwuuzCvJSC3JTFZIS01NSUpMzlYoSS0u4QIAUEsBAhQAFAAAAAgAKnhsXUOjeacaAAAAGAAAABMAAAAAAAAAAAAAAIABAAAAAGFzcGEtMjAyNi0xMS0xMi5sb2dQSwUGAAAAAAEAAQBBAAAASwAAAAAA', 'base64');

function setup(t, storeFeedback) {
  const DB = openDatabase(':memory:');
  t.after(() => DB.close());
  return (body = zip, headers = { 'Content-Type': 'application/zip' }) => worker.fetch(
    new Request('https://lineup.example.test/api/desktop/feedback', { method: 'POST', body, headers, duplex: 'half' }),
    { DB, SHARE_SECRET: Buffer.alloc(32, 1), clientIp: '192.0.2.1', storeFeedback }
  );
}

test('访客可提交 ZIP，反馈编号对应 COS 对象', async (t) => {
  const objects = new Map();
  const send = setup(t, async (key, body) => objects.set(key, body));
  const response = await send();
  assert.equal(response.status, 201);
  const { id } = await response.json();
  assert.match(id, /^[0-9a-f-]{36}$/);
  const [[key, body]] = objects;
  assert.match(key, /^feedback\/\d{4}-\d{2}-\d{2}\//);
  assert.ok(key.endsWith('/' + id + '.zip'));
  assert.deepEqual(body, zip);
});

test('不转存跨站请求、非 ZIP 和超过限制的请求体', async (t) => {
  const send = setup(t, () => assert.fail('请求不应到达 COS'));
  const headers = { 'Content-Type': 'application/zip' };
  assert.equal((await send(zip, { ...headers, Origin: 'https://other.example.test' })).status, 403);
  assert.equal((await send(zip, { 'Content-Type': 'text/plain' })).status, 415);
  assert.equal((await send(Buffer.from('plain text'))).status, 400);
  const oversized = new ReadableStream({ start(controller) {
    controller.enqueue(zip);
    controller.enqueue(Buffer.alloc(9 * 1024 * 1024));
    controller.close();
  } });
  assert.equal((await send(oversized)).status, 413);
});

test('COS 不可用时返回失败，同一来源的反馈次数受限', async (t) => {
  let writes = 0;
  const send = setup(t, async () => { writes++; throw new Error('offline'); });
  for (let i = 0; i < 5; i++) assert.equal((await send()).status, 503);
  assert.equal((await send()).status, 429);
  assert.equal(writes, 5);
  assert.equal((await setup(t)()).status, 503);
});
