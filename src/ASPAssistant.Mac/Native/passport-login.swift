import AppKit
import Foundation
import WebKit

@MainActor final class PassportWindow: NSObject, NSApplicationDelegate, NSWindowDelegate,
    WKNavigationDelegate, WKUIDelegate {
    private var window: NSWindow!
    private var webView: WKWebView!
    private var status: NSTextField!
    private var busy = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        let frame = NSRect(x: 0, y: 0, width: 840, height: 720)
        window = NSWindow(contentRect: frame, styleMask: [.titled, .closable, .resizable],
                          backing: .buffered, defer: false)
        window.title = "鹰角通行证 · ASP Assistant"
        window.center()
        window.delegate = self
        let container = NSView(frame: frame)
        window.contentView = container

        status = NSTextField(labelWithString: "请在鹰角官网登录，然后点击「读取战绩授权」。")
        status.frame = NSRect(x: 14, y: 12, width: 650, height: 26)
        container.addSubview(status)
        let grant = NSButton(title: "读取战绩授权", target: self, action: #selector(requestGrant))
        grant.frame = NSRect(x: 674, y: 9, width: 145, height: 30)
        grant.autoresizingMask = [.minXMargin, .maxYMargin]
        container.addSubview(grant)

        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        webView = WKWebView(frame: NSRect(x: 0, y: 47, width: 840, height: 673), configuration: config)
        webView.autoresizingMask = [.width, .height]
        webView.navigationDelegate = self
        webView.uiDelegate = self
        container.addSubview(webView)
        webView.load(URLRequest(url: URL(string: "https://user.hypergryph.com/login")!))
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func windowWillClose(_ notification: Notification) { NSApp.terminate(nil) }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        let url = navigationAction.request.url
        decisionHandler(url?.scheme == "https" && url?.host == "user.hypergryph.com" ? .allow : .cancel)
    }

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let url = navigationAction.request.url, url.scheme == "https",
           url.host == "user.hypergryph.com" { webView.load(navigationAction.request) }
        return nil
    }

    @objc private func requestGrant() {
        guard !busy, webView.url?.scheme == "https", webView.url?.host == "user.hypergryph.com" else {
            status.stringValue = "请先在鹰角官网登录。"
            return
        }
        busy = true
        status.stringValue = "正在读取一次性战绩授权…"
        let script = """
        if (location.origin !== 'https://user.hypergryph.com') return null;
        for (let i = 0; i < 40 && !window.HG_ACCOUNT_SDK?.User?.UI; i++)
            await new Promise(resolve => setTimeout(resolve, 250));
        const ui = window.HG_ACCOUNT_SDK?.User?.UI;
        if (typeof ui?.inheritSession !== 'function') return null;
        return await new Promise(resolve => {
            const timer = setTimeout(() => resolve(null), 90000);
            ui.inheritSession({ oauth: { appCode: '4ca99fa6b56cc2ba', grantType: 0 } },
                (status, data) => {
                    clearTimeout(timer);
                    resolve(status === 0 ? data?.oauthInfo?.code ?? null : null);
                });
        });
        """
        webView.callAsyncJavaScript(script, arguments: [:], in: nil, in: .page) { [weak self] result in
            guard let self else { return }
            self.busy = false
            switch result {
            case .success(let value):
                guard let code = value as? String, !code.isEmpty,
                      self.webView.url?.host == "user.hypergryph.com" else {
                    self.status.stringValue = "未取得授权。请确认已登录并重试。"
                    return
                }
                FileHandle.standardOutput.write(Data(("grant:" + code + "\n").utf8))
                NSApp.terminate(nil)
            case .failure:
                self.status.stringValue = "授权失败。请重新登录后重试。"
            }
        }
    }
}

MainActor.assumeIsolated {
    let app = NSApplication.shared
    app.setActivationPolicy(.regular)
    let delegate = PassportWindow()
    app.delegate = delegate
    app.run()
}
