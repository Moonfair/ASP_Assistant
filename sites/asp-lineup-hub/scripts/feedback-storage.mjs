import { Readable } from 'node:stream';
import COS from 'cos-nodejs-sdk-v5';

const bucket = 'aspa-log-1301636332';

export function createFeedbackStore(env) {
  const Region = env.COS_REGION;
  if (!Region) return null;
  const useDedicatedCredentials = env.COS_SECRET_ID || env.COS_SECRET_KEY;
  const SecretId = useDedicatedCredentials ? env.COS_SECRET_ID : env.TENCENTCLOUD_SECRET_ID;
  const SecretKey = useDedicatedCredentials ? env.COS_SECRET_KEY : env.TENCENTCLOUD_SECRET_KEY;
  if (!SecretId || !SecretKey) throw new Error('启用 COS 反馈时需要配置一对 COS 密钥。');

  const cos = new COS({ SecretId, SecretKey, Timeout: 120_000 });
  return async (Key, archive) => {
    // A one-shot stream prevents SDK retries from outlasting the desktop request.
    await cos.putObject({ Bucket: bucket, Region, Key, Body: Readable.from([archive]),
      ContentLength: archive.length, ContentType: 'application/zip' });
  };
}
