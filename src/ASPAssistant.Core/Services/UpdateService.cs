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

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
