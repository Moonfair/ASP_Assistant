namespace ASPAssistant.Core;

/// <summary>
/// Safe console writer for WPF / WinExe applications that may not have a
/// console handle attached.
///
/// All write operations are no-ops when stdout is unavailable (e.g. when the
/// process was launched from Explorer without a parent console), so callers
/// never need to guard against <see cref="System.IO.IOException"/>
/// "句柄无效".
/// </summary>
public static class AppConsole
{
    private static readonly bool _available;

    static AppConsole()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // Probe stdout to confirm the handle is valid.
            _ = Console.Out;
            _available = true;
        }
        catch
        {
            _available = false;
        }
    }

    public static void WriteLine(string message = "")
    {
        if (!_available) return;
        try { Console.WriteLine(message); }
        catch { }
    }

    public static void Write(string message)
    {
        if (!_available) return;
        try { Console.Write(message); }
        catch { }
    }
}
