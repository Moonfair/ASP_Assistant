import { createHash, createHmac, randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import nodemailer from 'nodemailer';

const subject = '卫戍阵容库登录验证码';
const message = (code) => '你的卫戍阵容库登录验证码为 ' + code + '，10 分钟内有效。若非本人操作，请忽略此邮件。';
const sha256 = (value) => createHash('sha256').update(value, 'utf8').digest('hex');
const sign = (key, value) => createHmac('sha256', key).update(value, 'utf8').digest();

function requireSetting(env, name) {
  const value = env[name]?.trim();
  if (!value) throw new Error('缺少配置 ' + name);
  return value;
}

export function sesRequest({ region, secretId, secretKey, from, templateId }, email, code, now = Date.now()) {
  const host = 'ses.tencentcloudapi.com';
  const contentType = 'application/json; charset=utf-8';
  const action = 'SendEmail';
  const algorithm = 'TC3-HMAC-SHA256';
  const signedHeaders = 'content-type;host;x-tc-action';
  const timestamp = Math.floor(now / 1000);
  const date = new Date(timestamp * 1000).toISOString().slice(0, 10);
  const scope = date + '/ses/tc3_request';
  const body = JSON.stringify({
    FromEmailAddress: from, Destination: [email], Subject: subject,
    Template: { TemplateID: templateId, TemplateData: JSON.stringify({ code }) }
  });
  const headersToSign = 'content-type:' + contentType + '\nhost:' + host + '\nx-tc-action:sendemail\n';
  const canonical = ['POST', '/', '', headersToSign, signedHeaders, sha256(body)].join('\n');
  const toSign = [algorithm, String(timestamp), scope, sha256(canonical)].join('\n');
  const key = sign(sign(sign('TC3' + secretKey, date), 'ses'), 'tc3_request');
  const signature = sign(key, toSign).toString('hex');
  const headers = {
    'Content-Type': contentType,
    Authorization: algorithm + ' Credential=' + secretId + '/' + scope + ', SignedHeaders=' + signedHeaders + ', Signature=' + signature,
    'X-TC-Action': action, 'X-TC-Version': '2020-10-02', 'X-TC-Region': region, 'X-TC-Timestamp': String(timestamp)
  };
  return { url: 'https://' + host + '/', options: { method: 'POST', headers, body, redirect: 'error', signal: AbortSignal.timeout(12_000) } };
}

export function createMailer(env, devOutbox) {
  if (devOutbox) {
    return async (email, code) => {
      await mkdir(devOutbox, { recursive: true, mode: 0o700 });
      const path = new URL(randomUUID() + '.json', devOutbox);
      await writeFile(path, JSON.stringify({ to: email, subject, code, text: message(code) }, null, 2),
        { encoding: 'utf8', mode: 0o600, flag: 'wx' });
    };
  }

  switch (env.MAIL_PROVIDER) {
    case 'disabled':
      return null;
    case 'smtp': {
      const host = requireSetting(env, 'SMTP_HOST');
      const port = Number(env.SMTP_PORT || 465);
      const from = requireSetting(env, 'MAIL_FROM');
      if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('SMTP_PORT 无效');
      const transport = nodemailer.createTransport({
        host, port, secure: port === 465, requireTLS: port !== 465,
        auth: { user: requireSetting(env, 'SMTP_USER'), pass: requireSetting(env, 'SMTP_PASSWORD') },
        connectionTimeout: 8_000, greetingTimeout: 8_000, socketTimeout: 12_000
      });
      return (email, code) => transport.sendMail({ from, to: email, subject, text: message(code) });
    }
    case 'ses': {
      const settings = {
        from: requireSetting(env, 'MAIL_FROM'),
        region: requireSetting(env, 'SES_REGION'),
        templateId: Number(requireSetting(env, 'SES_TEMPLATE_ID')),
        secretId: requireSetting(env, 'TENCENTCLOUD_SECRET_ID'),
        secretKey: requireSetting(env, 'TENCENTCLOUD_SECRET_KEY')
      };
      if (!Number.isSafeInteger(settings.templateId) || settings.templateId <= 0) throw new Error('SES_TEMPLATE_ID 无效');
      return async (email, code) => {
        const { url, options } = sesRequest(settings, email, code);
        const response = await fetch(url, options);
        const { Response: result } = await response.json();
        if (!response.ok || !result?.MessageId || result.Error) {
          throw Object.assign(new Error('SES 未接受邮件'), { code: result?.Error?.Code ?? 'SES_HTTP_' + response.status });
        }
      };
    }
    default:
      throw new Error('请设置 MAIL_PROVIDER 为 ses、smtp 或 disabled');
  }
}
