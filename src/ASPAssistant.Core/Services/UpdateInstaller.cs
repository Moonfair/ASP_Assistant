using System.Text;
using System.Text.Json;

namespace ASPAssistant.Core.Services;

internal static class UpdateInstaller
{
    private static string UpdateRoot => Path.Combine(Path.GetTempPath(), "ASPAssistant-Update");

    internal static string CreateJobDirectory()
    {
        var path = Path.Combine(UpdateRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ValidateJobDirectory(string jobDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(jobDirectory));
        if (!string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(UpdateRoot), StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(path), "N", out _))
            throw new ArgumentException("Not an owned update job directory.", nameof(jobDirectory));
        return path;
    }

    internal static void DeleteJobDirectory(string jobDirectory)
    {
        var path = ValidateJobDirectory(jobDirectory);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    internal static async Task<string> PrepareAsync(
        string sourceDirectory, string appDirectory, string jobDirectory, int processId,
        string logDirectory, CancellationToken ct = default)
    {
        var job = ValidateJobDirectory(jobDirectory);
        var source = Path.GetFullPath(sourceDirectory);
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        if (!source.StartsWith(job + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(source, "ASPAssistant.App.exe")))
            throw new InvalidDataException("更新包缺少 ASPAssistant.App.exe，无法安装。");
        if (string.Equals(destination, job, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(job + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新目标不能位于临时更新目录内。");

        // Fail while the current application can still report the error to the user.
        using (new FileStream(Path.Combine(destination, $".aspa-update-{Guid.NewGuid():N}.tmp"),
                   FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }

        var manifest = new
        {
            SourceDirectory = source, AppDirectory = destination, ProcessId = processId,
            ErrorLogPath = Path.Combine(Path.GetFullPath(logDirectory), "update-errors.log")
        };
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(Path.Combine(job, "update.json"), JsonSerializer.Serialize(manifest), encoding, ct);
        using var resource = typeof(UpdateInstaller).Assembly.GetManifestResourceStream("ASPAssistant.Core.UpdateInstaller.ps1")
            ?? throw new InvalidOperationException("Missing embedded updater.");
        using var reader = new StreamReader(resource);
        var script = await reader.ReadToEndAsync(ct);
        var scriptPath = Path.Combine(job, "update.ps1");
        await File.WriteAllTextAsync(scriptPath, script, encoding, ct);
        return scriptPath;
    }
}
