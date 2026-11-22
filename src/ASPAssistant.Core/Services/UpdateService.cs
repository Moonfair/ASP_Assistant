using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Services;

public record UpdateInfo(
    string TagName,
    string Version,
    string ReleaseNotes,
    string DownloadUrl,
    string AssetName)
{
    public bool IsBeta { get; internal init; }
    internal string? ExpectedSha256 { get; init; }
}

public class UpdateService
{
    private static readonly HttpClient DefaultHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "ASPAssistant-Updater/1.0" } }
    };

    private const string Owner = "Moonfair";
    private const string Repo = "ASP_Assistant";
    // Same bucket and versioned object path as the website's release workflow.
    private const string OssReleaseBaseUrl = "https://aspa.oss-cn-beijing.aliyuncs.com/releases/";
    private const string BetaBaseUrl = "https://aspa.oss-cn-beijing.aliyuncs.com/updates/beta/";
    private readonly HttpClient _http;
    private readonly ReleaseVersion _installed;

    public UpdateService() : this(DefaultHttpClient) { }

    internal UpdateService(HttpClient http, string? informationalVersion = null)
    {
        _http = http;
        var assembly = Assembly.GetEntryAssembly();
        var label = informationalVersion ?? assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _installed = ReleaseVersion.Parse(label?.Split('+', 2)[0])
            ?? ReleaseVersion.Parse(assembly?.GetName().Version?.ToString(3))
            ?? new(new Version(0, 0, 0), null);
    }

    public string CurrentVersionLabel => _installed.ToString();
    public Version CurrentVersion => _installed.BaseVersion;
    public bool IsDevBuild => CurrentVersion == new Version(0, 0, 0);

    // There is deliberately no persisted channel setting and no automatic caller.
    public async Task<UpdateInfo?> CheckForBetaUpdateAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(BetaBaseUrl + "latest.json", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var manifest = await response.Content.ReadFromJsonAsync<BetaManifest>(ct);
        var version = ReleaseVersion.FromTag(manifest?.Tag);
        if (manifest is null || version?.Beta is null || ParseSha256("sha256:" + manifest.Sha256) is not { } hash)
            throw new InvalidDataException("Beta 更新清单无效。");
        if (version.CompareTo(_installed) <= 0) return null;

        var name = SetupAssetName(manifest.Tag);
        return new UpdateInfo(manifest.Tag, version.ToString(), manifest.Notes ?? "", BetaBaseUrl + name, name)
        {
            IsBeta = true, ExpectedSha256 = hash
        };
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default, bool userInitiated = false)
    {
        // Skip update check in dev / IDE builds where no real version is embedded
        if (IsDevBuild && !userInitiated) return null;

        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            var release = await _http.GetFromJsonAsync<GitHubRelease>(url, ct);
            if (release is null || release.Prerelease || release.Draft) return null;

            var latestVersion = ReleaseVersion.FromTag(release.TagName);
            if (latestVersion is null || latestVersion.Beta.HasValue || latestVersion.CompareTo(_installed) <= 0)
                return null;

            var asset = release.Assets.FirstOrDefault(a => a.Name == SetupAssetName(release.TagName));
            if (asset is null) return null;

            return new UpdateInfo(
                release.TagName,
                latestVersion.ToString(),
                release.Body ?? string.Empty,
                asset.BrowserDownloadUrl,
                asset.Name)
            {
                ExpectedSha256 = ParseSha256(asset.Digest)
            };
        }
        catch when (!userInitiated)
        {
            return null;
        }
    }

    public async Task DownloadAndApplyUpdateAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = UpdateInstaller.CreateJobDirectory();
        var installerStarted = false;
        try
        {
            var scriptPath = await Task.Run(async () =>
            {
                await DownloadPackageAsync(info, Path.Combine(tempDir, UpdateInstaller.SetupFileName), progress, ct);
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ASPAssistant", "logs");
                return await UpdateInstaller.PrepareAsync(tempDir, Environment.ProcessId, logDir, ct);
            }, ct);

            // Launch on the caller's context so a dialog close cannot race this final check.
            progress?.Report(0.97);
            ct.ThrowIfCancellationRequested();

            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
                         "-NonInteractive", "-File", scriptPath, "-ShowErrorDialog" })
                start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)
                ?? throw new IOException("无法启动更新程序。");
            installerStarted = true;
            progress?.Report(1.0);
        }
        finally
        {
            // The helper waits for the wizard to finish before removing the download.
            if (!installerStarted)
            {
                try { await Task.Run(() => UpdateInstaller.DeleteJobDirectory(tempDir)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLogger.Error("Updater", "Could not clean failed download", ex);
                }
            }
        }
    }

    private static string SetupAssetName(string tag) => $"ASPAssistant-{tag}-win-x64-setup.exe";

    internal async Task DownloadPackageAsync(UpdateInfo info, string packagePath, IProgress<double>? progress,
        CancellationToken ct)
    {
        if (!info.IsBeta && info.ExpectedSha256 is not null && info.AssetName == SetupAssetName(info.TagName))
        {
            var ossUrl = OssReleaseBaseUrl + Uri.EscapeDataString(info.AssetName);
            try
            {
                await DownloadFileAsync(ossUrl, packagePath, info.ExpectedSha256, progress, ct);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested &&
                ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                AppLogger.Warn("Updater", $"OSS download failed, falling back to GitHub: {ex.Message}");
                progress?.Report(0);
            }
        }

        await DownloadFileAsync(info.DownloadUrl, packagePath, info.ExpectedSha256, progress, ct);
    }

    private async Task DownloadFileAsync(string url, string packagePath, string? expectedSha256,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(packagePath);
        using var hash = expectedSha256 is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long downloaded = 0;
        int bytesRead;
        while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            hash?.AppendData(buffer, 0, bytesRead);
            downloaded += bytesRead;
            if (totalBytes > 0) progress?.Report((double)downloaded / totalBytes * 0.96);
        }

        ct.ThrowIfCancellationRequested();
        if (hash is not null && !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expectedSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新包的 SHA-256 校验失败。");
    }

    private static string? ParseSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (digest?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) != true) return null;

        var value = digest[prefix.Length..];
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value : null;
    }

    private record BetaManifest(string Tag, string? Sha256, string? Notes);

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
