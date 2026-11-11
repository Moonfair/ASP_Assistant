const ASP_DOWNLOAD_URL = 'https://moonfair.github.io/ASP_Assistant/#download';

export function isWindowsDesktop(browser) {
  if (browser.userAgentData?.mobile) return false;
  if (browser.userAgentData?.platform) return browser.userAgentData.platform === 'Windows';
  return /\bWindows NT\b/i.test(browser.userAgent) && !/\b(?:Windows Phone|Android|Mobile)\b/i.test(browser.userAgent);
}

export async function desktopImportUri(share) {
  const bytes = new TextEncoder().encode(share.replace(/\r\n?/g, '\n'));
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  const hex = Array.from(hash, byte => byte.toString(16).padStart(2, '0')).join('');
  return 'aspassistant://import-lineup/?sha256=' + hex;
}

function openDesktopProtocol(uri) {
  return new Promise((resolve) => {
    const controller = new AbortController();
    const options = { signal: controller.signal };
    function finish(stayedOnPage) {
      clearTimeout(timeout);
      controller.abort();
      resolve(stayedOnPage);
    }
    // Browsers keep the page when the URI has no handler and expose no launch result.
    const timeout = setTimeout(() => finish(!document.hidden && document.hasFocus()), 4500);
    window.addEventListener('pagehide', () => finish(false), options);
    window.addEventListener('blur', () => finish(false), options);
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) finish(false);
    }, options);
    try { window.location.assign(uri); }
    catch { finish(true); }
  });
}

async function copyDesktopShare(share) {
  // Start the clipboard operation in the click gesture, including for cards that need to fetch text.
  if (typeof ClipboardItem === 'function' && navigator.clipboard?.write) {
    await navigator.clipboard.write([new ClipboardItem({
      'text/plain': share.then(text => new Blob([text], { type: 'text/plain' }))
    })]);
    return;
  }
  await navigator.clipboard.writeText(await share);
}
