using System.Reflection;

namespace ASPAssistant.Core;

/// <summary>
/// Lightweight file logger. Initialize once at startup with <see cref="Initialize"/>;
/// all subsequent calls to Info/Warn/Error are thread-safe.
/// Log files are written to the supplied directory as <c>aspa-yyyy-MM-dd.log</c>.
/// Files older than 7 days are deleted automatically on startup.
/// </summary>
public static class AppLogger
{
    private static string? _logFilePath;
    private static readonly object _lock = new();

    /// <summary>
    /// Call once at application startup. Creates the log directory, prunes old logs,
    /// and writes the first entry with version and runtime information.
    /// </summary>
    public static void Initialize(string logDir)
    {
        try
        {
            Directory.CreateDirectory(logDir);

            // Prune log files older than 7 days.
            foreach (var old in Directory.GetFiles(logDir, "aspa-*.log"))
            {
                if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-7))
                    File.Delete(old);
            }

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            _logFilePath = Path.Combine(logDir, $"aspa-{date}.log");

            var version = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";
            var runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

            WriteLine("INFO", "AppLogger", $"===== ASPAssistant {version} | {runtime} | {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLogger] Failed to initialise: {ex.Message}");
        }
    }

    public static void Info(string component, string message) =>
        WriteLine("INFO ", component, message);

    public static void Warn(string component, string message) =>
        WriteLine("WARN ", component, message);

    public static void Error(string component, string message) =>
        WriteLine("ERROR", component, message);

    public static void Error(string component, string message, Exception ex) =>
        WriteLine("ERROR", component, $"{message}: {ex}");

    private static void WriteLine(string level, string component, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{component}] {message}";
        System.Diagnostics.Debug.WriteLine(line);

        if (_logFilePath == null) return;
        lock (_lock)
        {
            try { File.AppendAllText(_logFilePath, line + Environment.NewLine); }
            catch { /* Never let logging crash the app */ }
        }
    }
}
