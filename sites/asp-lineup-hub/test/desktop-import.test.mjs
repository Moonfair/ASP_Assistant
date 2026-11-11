import assert from 'node:assert/strict';
import test from 'node:test';
import { desktopImportUri, isWindowsDesktop } from '../web/desktop-import.js';

test('只在 Windows 桌面浏览器启用客户端导入', () => {
  assert.equal(isWindowsDesktop({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' }), true);
  assert.equal(isWindowsDesktop({ userAgent: 'Mozilla/5.0 (Windows Phone 10.0; Android 6.0.1)' }), false);
  assert.equal(isWindowsDesktop({ userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)' }), false);
  assert.equal(isWindowsDesktop({ userAgentData: { platform: 'Windows', mobile: true } }), false);
  assert.equal(isWindowsDesktop({ userAgentData: { platform: 'Android', mobile: false } }), false);
});

test('启动链接只携带规范化剪贴板内容的 SHA-256', async () => {
  assert.equal(await desktopImportUri('ASPA-LINEUP/2\r\n阵容'),
    'aspassistant://import-lineup/?sha256=b281796e53b2234e8e7deb064521fbcaef39dfc2fc01f3627605feee22c71a8a');
});
