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
    private const string OssReleaseJsonUrl =
        $"https://moonfair.github.io/ASP_Assistant/oss-release.json";

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
            // Use oss-release.json (hosted on GitHub Pages) for version + OSS download URL
            var ossInfo = await _http.GetFromJsonAsync<OssReleaseInfo>(OssReleaseJsonUrl, ct);
            if (ossInfo is null || string.IsNullOrEmpty(ossInfo.Version) || string.IsNullOrEmpty(ossInfo.Url))
                return null;

            var latestVersion = ParseVersion(ossInfo.Version);
            if (latestVersion <= CurrentVersion) return null;

            // Best-effort: fetch release notes from GitHub API
            var notes = await FetchReleaseNotesAsync(ossInfo.Version, ct);

            var assetName = ossInfo.Url.Split('/').Last();

            return new UpdateInfo(
                ossInfo.Version,
                latestVersion.ToString(),
                notes,
                ossInfo.Url,
                assetName);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> FetchReleaseNotesAsync(string tag, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/tags/{tag}";
            var release = await _http.GetFromJsonAsync<GitHubRelease>(url, cts.Token);
            return release?.Body ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task DownloadAndApplyUpdateAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ASPAssistant-Update");
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        var zipPath = Path.Combine(tempDir, info.AssetName);

        // Download with progress reporting
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
                if (totalBytes > 0)
                    progress?.Report((double)downloaded / totalBytes * 0.9);
            }
        }

        progress?.Report(0.92);

        // Extract zip
        var extractDir = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        progress?.Report(0.97);

        // Resolve the source directory inside the zip
        // release.yml zips "publish/win-x64", so the zip contains a "win-x64" root folder
        var subDirs = Directory.GetDirectories(extractDir);
        var sourceDir = subDirs.Length == 1 ? subDirs[0] : extractDir;

        var appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var exePath = Path.Combine(appDir, "ASPAssistant.App.exe");
        var pid = Environment.ProcessId;

        // Write a PowerShell updater script to run after the app exits
        var scriptPath = Path.Combine(tempDir, "update.ps1");
        var scriptContent = $$"""
            $appPid = {{pid}}
            Write-Host "等待 ASPAssistant 退出..."
            $timeout = 30
            $elapsed = 0
            while ((Get-Process -Id $appPid -ErrorAction SilentlyContinue) -and ($elapsed -lt $timeout)) {
                Start-Sleep -Milliseconds 500
                $elapsed += 0.5
            }
            Start-Sleep -Milliseconds 500
            Write-Host "复制新文件到: {{appDir}}"
            try {
                Copy-Item -Path "{{sourceDir}}\*" -Destination "{{appDir}}" -Recurse -Force -ErrorAction Stop
                Write-Host "更新完成，正在重启..."
                Start-Process "{{exePath}}"
            } catch {
                Write-Host "更新失败: $_"
                [System.Windows.Forms.MessageBox]::Show("更新时发生错误: $_", "ASPAssistant 更新失败")
            }
            """;

        await File.WriteAllTextAsync(scriptPath, scriptContent, ct);
        progress?.Report(1.0);

        // Launch the update script as a detached background process
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -NonInteractive -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static Version ParseVersion(string tag)
    {
        var v = tag.TrimStart('v');
        return Version.TryParse(v, out var result) ? result : new Version(0, 0, 0);
    }

    private record OssReleaseInfo(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("url")] string Url);

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("body")] string? Body);
}
