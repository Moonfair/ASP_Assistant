using System.Diagnostics;
using System.Text.Json;

namespace ASPAssistant.Core.Interop;

public sealed record MacWindowInfo(nint Id, uint ProcessId, string Title, string Owner, RECT Bounds);
public sealed record MacWindowSnapshot(IReadOnlyList<MacWindowInfo> Windows, nint ForegroundWindow);

public static class MacWindowBridge
{
    private static readonly object Gate = new();
    private static MacWindowSnapshot _cached = new([], 0);
    private static DateTimeOffset _validUntil;

    public static MacWindowSnapshot Current()
    {
        lock (Gate)
        {
            if (DateTimeOffset.UtcNow < _validUntil) return _cached;
            _cached = ReadSnapshot();
            _validUntil = DateTimeOffset.UtcNow.AddMilliseconds(200);
            return _cached;
        }
    }

    private static MacWindowSnapshot ReadSnapshot()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "aspa-window-list");
        if (!File.Exists(executable)) return new([], 0);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process == null) return new([], 0);
            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return new([], 0);
            }
            var output = outputTask.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? Parse(output) : new([], 0);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            return new([], 0);
        }
    }

    public static MacWindowSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        uint foregroundPid = root.GetProperty("frontmostPid").GetUInt32();
        var windows = new List<MacWindowInfo>();
        foreach (var item in root.GetProperty("windows").EnumerateArray())
        {
            int width = item.GetProperty("width").GetInt32();
            int height = item.GetProperty("height").GetInt32();
            if (width <= 0 || height <= 0) continue;
            int x = item.GetProperty("x").GetInt32();
            int y = item.GetProperty("y").GetInt32();
            var title = item.GetProperty("title").GetString() ?? "";
            var owner = item.GetProperty("owner").GetString() ?? "";
            windows.Add(new MacWindowInfo((nint)item.GetProperty("id").GetInt64(),
                item.GetProperty("pid").GetUInt32(), title, owner,
                new RECT { Left = x, Top = y, Right = x + width, Bottom = y + height }));
        }
        nint foreground = windows.FirstOrDefault(window => window.ProcessId == foregroundPid)?.Id ?? 0;
        return new(windows, foreground);
    }

    public static MacWindowInfo? FindArknightsWindow(MacWindowSnapshot snapshot) =>
        snapshot.Windows.FirstOrDefault(item =>
            string.Equals(item.Owner, "明日方舟", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Title, "明日方舟", StringComparison.OrdinalIgnoreCase));
}

#if NET9_0
public static class User32
{
    public static nint GetRootWindow(nint window) => window;
    public static uint GetWindowThreadProcessId(nint window, out uint processId)
    {
        processId = MacWindowBridge.Current().Windows.FirstOrDefault(item => item.Id == window)?.ProcessId ?? 0;
        return processId;
    }
    public static bool IsWindow(nint window) => MacWindowBridge.Current().Windows.Any(item => item.Id == window);
    public static bool IsWindowVisible(nint window) => IsWindow(window);
    public static bool IsIconic(nint window) => !IsWindow(window);
    public static nint GetForegroundWindow() => MacWindowBridge.Current().ForegroundWindow;
    public static IReadOnlyList<(nint Handle, string Title, uint ProcessId)> GetTopLevelWindows() =>
        MacWindowBridge.Current().Windows.Select(item =>
            (item.Id, string.IsNullOrWhiteSpace(item.Title) ? item.Owner : item.Title, item.ProcessId)).ToArray();
    public static bool GetWindowRect(nint window, out RECT rect)
    {
        var found = MacWindowBridge.Current().Windows.FirstOrDefault(item => item.Id == window);
        rect = found?.Bounds ?? default;
        return found != null;
    }
    public static RECT? GetClientRectScreen(nint window) =>
        MacWindowBridge.Current().Windows.FirstOrDefault(item => item.Id == window)?.Bounds;
    public static nint FindRenderChild(nint window, RECT bounds, int width, int height) => 0;
    public static nint FindArknightsWindow() =>
        MacWindowBridge.FindArknightsWindow(MacWindowBridge.Current())?.Id ?? 0;
}
#endif
