using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Services;

public record UpdateInfo(
    string TagName,
    string Version,
    string ReleaseNotes,
    string DownloadUrl,
    string AssetName);

public class UpdateService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "ASPAssistant-Updater/1.0" } }
    };

    private const string Owner = "Moonfair";
    private const string Repo = "ASP_Assistant";

    public Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// 返回 true 表示当前是开发构建（版本号为 0.0.0），不进行更新检查。
    /// </summary>
    public bool IsDevBuild => CurrentVersion == new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        // Skip update check in dev / IDE builds where no real version is embedded
        if (IsDevBuild) return null;

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
                asset.Name);
        }
        catch
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
            using var response = await _http.GetAsync(
                info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = File.Create(zipPath))
            {
                var buffer = new byte[65536];
                long downloaded = 0;
                int bytesRead;
                while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloaded += bytesRead;
                    if (totalBytes > 0) progress?.Report((double)downloaded / totalBytes * 0.9);
                }
            }

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
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
