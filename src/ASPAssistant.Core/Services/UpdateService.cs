using System.IO;
using System.IO.Compression;
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
    private readonly HttpClient _http;

    public UpdateService() : this(DefaultHttpClient) { }

    internal UpdateService(HttpClient http) => _http = http;

    public Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// 返回 true 表示当前是开发构建（版本号为 0.0.0），跳过自动更新检查。
    /// </summary>
    public bool IsDevBuild => CurrentVersion <= new Version(0, 0, 0, 0);

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default, bool userInitiated = false)
    {
        // Skip update check in dev / IDE builds where no real version is embedded
        if (IsDevBuild && !userInitiated) return null;

        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            var release = await _http.GetFromJsonAsync<GitHubRelease>(url, ct);
            if (release is null) return null;

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion <= CurrentVersion) return null;

            var asset = release.Assets.FirstOrDefault(a => a.Name.Contains("win-x64") && a.Name.EndsWith(".zip"));
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
            // Use a fixed local name: release metadata is never interpreted as a path.
            var zipPath = Path.Combine(tempDir, "update.zip");
            await DownloadPackageAsync(info, zipPath, progress, ct);

            progress?.Report(0.92);
            var extractDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            ct.ThrowIfCancellationRequested();
            var sourceDir = File.Exists(Path.Combine(extractDir, "ASPAssistant.App.exe"))
                ? extractDir : Path.Combine(extractDir, "win-x64");
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ASPAssistant", "logs");
            var scriptPath = await UpdateInstaller.PrepareAsync(
                sourceDir, AppContext.BaseDirectory, tempDir, Environment.ProcessId, logDir, ct);
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
            // Once launched, the installer owns cleanup, including copy/restart failures.
            if (!installerStarted)
            {
                try { UpdateInstaller.DeleteJobDirectory(tempDir); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLogger.Error("Updater", "Could not clean failed download", ex);
                }
            }
        }
    }

    internal async Task DownloadPackageAsync(UpdateInfo info, string zipPath, IProgress<double>? progress,
        CancellationToken ct)
    {
        if (info.ExpectedSha256 is not null && info.AssetName == $"ASPAssistant-{info.TagName}-win-x64.zip")
        {
            var ossUrl = OssReleaseBaseUrl + Uri.EscapeDataString(info.AssetName);
            try
            {
                await DownloadFileAsync(ossUrl, zipPath, info.ExpectedSha256, progress, ct);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested &&
                ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                AppLogger.Warn("Updater", $"OSS download failed, falling back to GitHub: {ex.Message}");
                progress?.Report(0);
            }
        }

        await DownloadFileAsync(info.DownloadUrl, zipPath, info.ExpectedSha256, progress, ct);
    }

    private async Task DownloadFileAsync(string url, string zipPath, string? expectedSha256,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(zipPath);
        using var hash = expectedSha256 is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long downloaded = 0;
        int bytesRead;
        while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            hash?.AppendData(buffer, 0, bytesRead);
            downloaded += bytesRead;
            if (totalBytes > 0) progress?.Report((double)downloaded / totalBytes * 0.89);
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

    private static Version ParseVersion(string tag)
    {
        var v = tag.TrimStart('v');
        return Version.TryParse(v, out var result) ? result : new Version(0, 0, 0);
    }

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
