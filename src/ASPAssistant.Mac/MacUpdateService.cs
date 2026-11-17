using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ASPAssistant.Mac;

internal sealed class MacUpdateService(HttpClient? client = null)
{
    private static readonly HttpClient Shared = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "ASPAssistant-macOS/0.1" } }
    };
    private readonly HttpClient _http = client ?? Shared;

    public Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);

    public async Task<MacRelease?> CheckAsync(CancellationToken token = default)
    {
        var release = await _http.GetFromJsonAsync<Release>(
            "https://api.github.com/repos/Moonfair/ASP_Assistant/releases/latest", token);
        if (release == null || !Version.TryParse(release.TagName.TrimStart('v'), out var latest) ||
            latest <= CurrentVersion) return null;
        var asset = release.Assets.FirstOrDefault(item =>
            item.Name.Contains("osx-arm64", StringComparison.OrdinalIgnoreCase) &&
            (item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
             item.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)));
        return asset == null ? null : new MacRelease(release.TagName, asset.BrowserDownloadUrl);
    }

    private sealed record Release([property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] List<Asset> Assets);
    private sealed record Asset([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}

internal sealed record MacRelease(string Tag, string DownloadUrl);
