using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

public sealed class GameTargetSelection
{
    private GameTarget? _current;
    public GameTarget? Current => Volatile.Read(ref _current);
    public GameTargetPreference Preference { get; private set; } = new();
    public event Action? Changed;

    public void Select(GameTargetPreference preference, GameTarget? target = null)
    {
        Preference = preference;
        SetCurrent(target);
    }

    // Keep the same snapshot reference during polling so in-flight frames retain their identity.
    public void SetCurrent(GameTarget? target)
    {
        if (Current == target) return;
        Volatile.Write(ref _current, target);
        Changed?.Invoke();
    }

    public static GameTarget? FindWindowsClient()
    {
        var window = User32.FindArknightsWindow();
        if (window == 0) return null;
        User32.GetWindowThreadProcessId(window, out var pid);
        return new GameTarget(GameTarget.WindowsId, "Windows 客户端",
            GameTargetKind.Windows, window, window, WindowPid: pid);
    }

    internal static GameTarget? ChooseAutomaticTarget(GameTarget? windows, IReadOnlyList<GameTarget> emulators,
        GameTarget? current, nint foreground)
    {
        if (windows?.Window == foreground) return windows;
        var focusedEmulator = emulators.FirstOrDefault(target => target.Window == foreground);
        if (focusedEmulator != null) return focusedEmulator;

        if (windows != null && current?.Kind == GameTargetKind.Windows) return windows;
        var previousEmulator = emulators.FirstOrDefault(target =>
            string.Equals(target.Id, current?.Id, StringComparison.OrdinalIgnoreCase));
        return previousEmulator ?? windows ?? emulators.FirstOrDefault();
    }

    public static IReadOnlyList<HostWindow> ListWindows()
    {
        var windows = new List<HostWindow>();
        foreach (var window in User32.GetTopLevelWindows())
        {
            try
            {
                using var process = Process.GetProcessById((int)window.ProcessId);
                if (process.MainModule?.FileName is { } path)
                    windows.Add(new HostWindow(window.Handle, window.Title, path, window.ProcessId));
            }
            catch (Exception ex) when (ex is ArgumentException or Win32Exception or InvalidOperationException) { }
        }
        return windows;
    }

    public static GameTarget? FindManualTarget(GameTargetPreference preference, IReadOnlyList<HostWindow> windows)
    {
        var matching = windows.Where(w => w.Title == preference.WindowTitle &&
            string.Equals(w.ProcessPath, preference.ProcessPath, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matching.Length == 1 ? FromManualWindow(preference, matching[0]) : null;
    }

    public static GameTarget FromManualWindow(GameTargetPreference preference, HostWindow window) =>
        new("adb", window.Title, GameTargetKind.Adb, window.Handle, window.Handle,
            preference.AdbPath, preference.Serial, WindowPid: window.ProcessId);

    public static bool IsLocalAdbSerial(string serial)
    {
        if (Regex.IsMatch(serial, @"\Aemulator-[0-9]{1,5}\z"))
            return int.TryParse(serial.AsSpan(9), out int emulatorPort) && emulatorPort is > 0 and <= 65535;
        return IPEndPoint.TryParse(serial, out var endpoint) && endpoint.Port != 0 && IPAddress.IsLoopback(endpoint.Address);
    }
}

public sealed record HostWindow(nint Handle, string Title, string ProcessPath, uint ProcessId)
{
    public string Label => $"{Title} · {Path.GetFileNameWithoutExtension(ProcessPath)}";
}
