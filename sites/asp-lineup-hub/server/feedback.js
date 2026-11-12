const MAX_FEEDBACK_BYTES = 9 * 1024 * 1024;
const FEEDBACK_WINDOW = 60 * 60_000;

async function submitFeedback(request, { DB: db, clientIp, storeFeedback }) {
  if (request.headers.has('origin') || request.headers.has('sec-fetch-site')) {
    throw new HttpError(403, '请在 ASP Assistant 客户端内发送反馈。');
  }
  if (!storeFeedback) throw new HttpError(503, '反馈服务暂不可用，请稍后重试。');
  if (!/^application\/zip(?:\s*;|$)/i.test(request.headers.get('content-type') ?? '') || !request.body) {
    throw new HttpError(415, '请提交 ZIP 日志压缩包。');
  }
  const tooLarge = () => new HttpError(413, '日志压缩包过大。');
  if (Number(request.headers.get('content-length')) > MAX_FEEDBACK_BYTES) throw tooLarge();

  const now = Date.now();
  const limitMessage = '反馈提交过于频繁，请稍后重试。';
  await takeAuthLimit(db, 'feedback-ip:' + (clientIp || 'unknown'), now + FEEDBACK_WINDOW, 5, now, limitMessage);
  await db.prepare('DELETE FROM auth_limits WHERE expires_at <= ?').bind(now).run();

  const reader = request.body.getReader();
  const chunks = [];
  let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > MAX_FEEDBACK_BYTES) {
        await reader.cancel();
        throw tooLarge();
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  const archive = Buffer.concat(chunks, size);
  if (size < 30 || archive.readUInt32LE(0) !== 0x04034b50) {
    throw new HttpError(400, '日志压缩包格式无效。');
  }

  await takeAuthLimit(db, 'feedback-global', now + FEEDBACK_WINDOW, 100, now, limitMessage);

  const id = crypto.randomUUID();
  const date = new Date(now).toISOString().slice(0, 10);
  try {
    await storeFeedback(`feedback/${date}/${id}.zip`, archive);
  } catch (error) {
    const reason = String(error?.code ?? error?.statusCode ?? 'provider');
    console.error('Feedback storage failed:', /^[\w.-]{1,64}$/.test(reason) ? reason : 'provider');
    throw new HttpError(503, '日志暂时无法保存，请稍后重试。');
  }
  return reply({ id }, 201);
}
