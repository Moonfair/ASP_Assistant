import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { isIP } from 'node:net';
import { dirname, resolve } from 'node:path';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';
import { openDatabase } from './local-db.mjs';
import { createMailer } from './mail.mjs';
import app from '../dist/server/index.js';

const development = process.argv.includes('--development');
const port = Number(process.env.PORT || 4173);
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('PORT 无效');
const host = development ? '127.0.0.1' : process.env.BIND_HOST || '127.0.0.1';
const origin = development ? 'http://127.0.0.1:' + port : process.env.PUBLIC_ORIGIN;
if (!origin || new URL(origin).origin !== origin || (!development && !origin.startsWith('https://'))) {
  throw new Error('PUBLIC_ORIGIN 必须是完整 HTTPS 域名，例如 https://lineup.example.com');
}
const devOutbox = development ? new URL('../.site-data/mail/', import.meta.url) : null;
const sendCode = createMailer(process.env, devOutbox);
const secret = development ? randomBytes(32) : Buffer.from(process.env.AUTH_SECRET || '', 'base64');
if (sendCode && secret.length < 32) throw new Error('AUTH_SECRET 至少需要 32 字节随机数据（Base64）');

const dataPath = process.env.LINEUP_DB ? resolve(process.env.LINEUP_DB)
  : fileURLToPath(new URL('../.site-data/lineups.sqlite', import.meta.url));
mkdirSync(dirname(dataPath), { recursive: true, mode: 0o700 });
const DB = openDatabase(dataPath);
const AUTH = sendCode ? { secret, sendCode } : null;
const trustProxy = !development && process.env.TRUST_PROXY === '1';

const server = createServer(async (incoming, outgoing) => {
  try {
    if (!incoming.url?.startsWith('/') || incoming.url.startsWith('//')) {
      outgoing.writeHead(400).end();
      return;
    }
    const url = new URL(incoming.url, origin);
    if (url.origin !== origin) {
      outgoing.writeHead(400).end();
      return;
    }
    const method = incoming.method;
    const request = new Request(url, {
      method, headers: incoming.headers,
      ...(method === 'GET' || method === 'HEAD' ? {} : { body: Readable.toWeb(incoming), duplex: 'half' })
    });
    const proxyIp = incoming.headers['x-real-ip'];
    const clientIp = trustProxy && typeof proxyIp === 'string' && isIP(proxyIp) ? proxyIp : incoming.socket.remoteAddress;
    const response = await app.fetch(request, { DB, AUTH, clientIp });
    const headers = Object.fromEntries(response.headers);
    const cookies = response.headers.getSetCookie();
    if (cookies.length) headers['set-cookie'] = cookies;
    outgoing.writeHead(response.status, headers);
    if (response.body) await pipeline(Readable.fromWeb(response.body), outgoing);
    else outgoing.end();
  } catch (error) {
    if (outgoing.destroyed) return;
    console.error('HTTP request failed:', error?.code ?? error?.name ?? 'error');
    if (!outgoing.headersSent) outgoing.writeHead(500);
    outgoing.end();
  }
});
server.requestTimeout = 30_000;
server.headersTimeout = 10_000;
server.listen(port, host, () => {
  console.log((development ? '本地开发：' : '阵容库启动：') + origin);
  if (devOutbox) console.log('开发验证码邮件写入 .site-data/mail/');
});
for (const signal of ['SIGTERM', 'SIGINT']) server.once(signal, () => server.close(() => DB.close()));
