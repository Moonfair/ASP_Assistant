using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace ASPAssistant.App.Services;

internal static class HypergryphPassport
{
    internal static readonly Uri LoginUri = new("https://user.hypergryph.com/login");

    internal static bool IsPassportPage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Host == LoginUri.Host;

    internal static bool IsSessionSaved(string url, string method, int status) =>
        method == "POST" && status is >= 200 and < 300 &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Host == "web-api.hypergryph.com" && uri.AbsolutePath == "/account/info/hg";

    internal static async Task<string?> RequestHistoryGrantAsync(CoreWebView2 webView, CancellationToken cancellationToken)
    {
        if (!IsPassportPage(webView.Source)) return null;

        // The official SDK handles the passport session and any provider consent UI.
        // Transfer only the one-use code for the Skland service.
        const string script = """
            (async () => {
                if (location.origin !== "https://user.hypergryph.com") return null;
                for (let attempt = 0; attempt < 40 && !window.HG_ACCOUNT_SDK?.User?.UI; attempt++) {
                    await new Promise(resolve => setTimeout(resolve, 250));
                }
                const accountUi = window.HG_ACCOUNT_SDK?.User?.UI;
                if (typeof accountUi?.inheritSession !== "function") return { sdkReady: false };
                return new Promise(resolve => {
                    const timeout = setTimeout(() => resolve({ timeout: true }), 90000);
                    accountUi.inheritSession({ oauth: { appCode: "4ca99fa6b56cc2ba", grantType: 0 } },
                        (status, data) => {
                            clearTimeout(timeout);
                            resolve({ code: status === 0 ? data?.oauthInfo?.code ?? null : null });
                        });
                });
            })()
            """;
        var arguments = JsonSerializer.Serialize(new { expression = script, awaitPromise = true, returnByValue = true });
        var result = await webView.CallDevToolsProtocolMethodAsync("Runtime.evaluate", arguments).WaitAsync(cancellationToken);
        using var json = JsonDocument.Parse(result);
        if (!json.RootElement.TryGetProperty("result", out var response) ||
            !response.TryGetProperty("value", out var value) ||
            value.ValueKind != JsonValueKind.Object || !IsPassportPage(webView.Source))
            return null;
        if (value.TryGetProperty("sdkReady", out var ready) && ready.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException("鹰角登录组件仍在加载，请稍后重试。");
        if (value.TryGetProperty("timeout", out _))
            throw new TimeoutException("鹰角授权已超时，请重试。");
        return value.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString()
            : null;
    }
}
