using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public static partial class LineupPublicLink
{
    public static Uri HomePage { get; } = new("https://aspa.moonfair.cn/");
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 650_000
    };
    private static readonly JsonSerializerOptions RequestJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static bool TryGetToken(string text, out string token)
    {
        token = "";
        if (text.Length > 256 || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || !uri.IdnHost.Equals(HomePage.IdnHost, StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;
        var match = LinkPath().Match(uri.AbsolutePath);
        if (!match.Success) return false;
        token = match.Groups[1].Value;
        return true;
    }

    public static string Canonicalize(string text) => TryGetToken(text, out var token)
        ? new Uri(HomePage, "s/" + token).AbsoluteUri
        : throw new FormatException("请粘贴卫戍阵容库生成的阵容短链接。");

    public static Task<string> CreateAsync(string share) => CreateAsync(share, Client);

    internal static async Task<string> CreateAsync(string share, HttpClient client)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(new Uri(HomePage, "api/share"), new { share }, RequestJson);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable)
                throw new FormatException("作业站的短链接服务暂不可用，请稍后重试。");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ShareError>();
                throw new FormatException(error?.Error ?? "无法创建阵容短链接，请稍后重试。");
            }
            var result = await response.Content.ReadFromJsonAsync<CreatedLink>();
            if (result?.ShortPath is not { } path || !SnapshotPath().IsMatch(path))
                throw new FormatException("作业站返回的阵容短链接无效。");
            return new Uri(HomePage, path).AbsoluteUri;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new FormatException("暂时无法创建阵容短链接，请检查网络后重试。", ex);
        }
    }

    public static Task<string> ResolveAsync(string text) => ResolveAsync(text, Client);

    internal static async Task<string> ResolveAsync(string text, HttpClient client)
    {
        if (!TryGetToken(text, out var token))
            throw new FormatException("请粘贴卫戍阵容库生成的阵容短链接。");
        try
        {
            using var response = await client.GetAsync(new Uri(HomePage, "api/share/" + token));
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new FormatException("这个短链接对应的阵容不存在。");
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ResolvedLink>();
            if (result?.Share is not { Length: > 0 and <= LineupShareService.MaxShareTextLength } share
                || result.ShortPath != "/s/" + token)
                throw new FormatException("网站返回的阵容内容无效。");
            return share;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new FormatException("暂时无法读取阵容短链接，请检查网络后重试。", ex);
        }
    }

    private sealed record ResolvedLink(string Share, string ShortPath);
    private sealed record CreatedLink(string ShortPath);
    private sealed record ShareError(string Error);

    [GeneratedRegex(@"\A/s/u-[A-Za-z0-9_-]{22}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SnapshotPath();

    [GeneratedRegex(@"\A/s/(u-[A-Za-z0-9_-]{22}|[A-Za-z0-9_-]{22}|demo-[a-z]{1,20})\z", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPath();
}
