using System.Diagnostics;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

internal static class MacScreenCapture
{
    public static byte[]? CaptureRect(RECT rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        var path = Path.Combine(Path.GetTempPath(), $"aspa-capture-{Guid.NewGuid():N}.png");
        try
        {
            var start = new ProcessStartInfo("/usr/sbin/screencapture")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-x");
            start.ArgumentList.Add("-t");
            start.ArgumentList.Add("png");
            start.ArgumentList.Add($"-R{rect.Left},{rect.Top},{rect.Width},{rect.Height}");
            start.ArgumentList.Add(path);
            using var process = Process.Start(start);
            if (process == null) return null;
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            if (process.ExitCode != 0 || !File.Exists(path))
            {
                AppLogger.Warn("MacCapture", "screencapture failed: " +
                    process.StandardError.ReadToEnd().Trim());
                return null;
            }
            var bytes = File.ReadAllBytes(path);
            return ScreenCaptureService.GetPngSize(bytes) != default ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            AppLogger.Warn("MacCapture", ex.Message);
            return null;
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
