const glyphs = {
  A:'0E11111F111111', B:'1E11111E11111E', C:'0F10101010100F', D:'1E11111111111E',
  E:'1F10101E10101F', F:'1F10101E101010', G:'0F10101311110F', H:'1111111F111111',
  J:'0702020212120C', K:'11121418141211', L:'1010101010101F', M:'111B1515111111',
  N:'11191513111111', P:'1E11111E101010', Q:'0E11111115120D', R:'1E11111E141211',
  S:'0F10100E01011E', T:'1F040404040404', U:'1111111111110E', V:'11111111110A04',
  W:'1111111515150A', X:'11110A040A1111', Y:'11110A04040404', Z:'1F01020408101F',
  2:'0E11010204081F', 3:'1E01010601011E', 4:'02060A121F0202', 5:'1F101E0101110E',
  6:'0608101E11110E', 7:'1F010204080808', 8:'0E11110E11110E', 9:'0E11110F01020C'
};
const alphabet = Object.keys(glyphs);
const encode = new TextEncoder();
const random = (length) => crypto.getRandomValues(new Uint8Array(length));

async function digest(value) {
  const bytes = new Uint8Array(await crypto.subtle.digest('SHA-256', encode.encode(value)));
  return Array.from(bytes, n => n.toString(16).padStart(2, '0')).join('');
}

function pngChunk(type, bytes) {
  const result = new Uint8Array(12 + bytes.length);
  const view = new DataView(result.buffer);
  view.setUint32(0, bytes.length);
  result.set(encode.encode(type), 4);
  result.set(bytes, 8);
  let crc = -1;
  for (const byte of result.subarray(4, -4)) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
  }
  view.setUint32(result.length - 4, ~crc);
  return result;
}

async function captchaImage(answer) {
  const width = 168, height = 52, stride = width + 1;
  const pixels = new Uint8Array(stride * height).fill(245);
  for (let y = 0; y < height; y++) pixels[y * stride] = 0;
  const plot = (x, y, color) => {
    if (x >= 0 && x < width && y >= 0 && y < height) pixels[y * stride + x + 1] = color;
  };
  const offsets = random(20);
  for (let i = 0; i < answer.length; i++) {
    const rows = glyphs[answer[i]].match(/../g).map(hex => parseInt(hex, 16));
    const x0 = 11 + 30 * i + offsets[i] % 8;
    const y0 = 9 + offsets[5 + i] % 7;
    const angle = (offsets[10 + i] % 29 - 14) / 50;
    const color = 26 + offsets[15 + i] % 46;
    for (let y = 0; y < 28; y++) {
      for (let x = 0; x < 20; x++) {
        if (rows[y >> 2] & (16 >> (x >> 2))) {
          plot(Math.round(x0 + x - (y - 14) * angle), Math.round(y0 + y + (x - 10) * angle), color);
        }
      }
    }
  }
  const noise = random(740);
  for (let i = 0; i < 720; i += 3) plot(noise[i] % width, noise[i + 1] % height, 112 + noise[i + 2] % 105);
  for (let x = 0; x < width; x++) {
    for (let line = 0; line < 3; line++) {
      const y = 10 + 13 * line + Math.round(Math.sin(x / (13 + line * 5) + noise[720 + line]) * 8);
      plot(x, y, 95 + line * 30);
    }
  }
  const header = new Uint8Array(13);
  const view = new DataView(header.buffer);
  view.setUint32(0, width);
  view.setUint32(4, height);
  header[8] = 8;
  const compressed = new Uint8Array(await new Response(
    new Blob([pixels]).stream().pipeThrough(new CompressionStream('deflate'))
  ).arrayBuffer());
  const sections = [[137,80,78,71,13,10,26,10], pngChunk('IHDR', header),
    pngChunk('IDAT', compressed), pngChunk('IEND', new Uint8Array())];
  return 'data:image/png;base64,' + btoa(sections.map(part => String.fromCharCode(...part)).join(''));
}

async function issueCaptcha(db, clientIp, visitor, action, target) {
  const now = Date.now();
  const subject = clientIp || visitor;
  const key = await digest(subject + ':' + Math.floor(now / 60_000));
  const { attempts } = await db.prepare(
    'INSERT INTO challenge_limits VALUES (?, ?, 1) ON CONFLICT(key) DO UPDATE SET attempts = attempts + 1 RETURNING attempts'
  ).bind(key, now + 60_000).first();
  if (attempts > 30) throw new HttpError(429, '验证码请求过多，请稍后重试。');
  await db.prepare('DELETE FROM challenges WHERE expires <= ?').bind(now).run();
  await db.prepare('DELETE FROM challenge_limits WHERE expires <= ?').bind(now).run();
  const id = crypto.randomUUID();
  const answer = Array.from(random(5), n => alphabet[n % alphabet.length]).join('');
  await db.prepare('INSERT INTO challenges VALUES (?, ?, ?, ?, ?, ?)').bind(
    id, visitor, action, target, await digest(id + ':' + answer), now + 5 * 60_000
  ).run();
  return { id, image: await captchaImage(answer) };
}

async function verifyCaptcha(db, visitor, action, target, captcha) {
  const id = captcha?.id;
  const answer = typeof captcha?.answer === 'string' ? captcha.answer.trim().toUpperCase() : '';
  if (!validId(id) || !/^[A-Z2-9]{5}$/.test(answer)) {
    throw new HttpError(400, '请输入图片中的 5 位验证码。', 'captcha');
  }
  const challenge = await db.prepare(
    'DELETE FROM challenges WHERE id = ? AND visitor = ? AND action = ? AND target = ? AND expires > ? RETURNING answer_hash'
  ).bind(id, visitor, action, target, Date.now()).first();
  if (challenge?.answer_hash !== await digest(id + ':' + answer)) {
    throw new HttpError(400, '验证码错误、已使用或已过期，请换一张重试。', 'captcha');
  }
}
