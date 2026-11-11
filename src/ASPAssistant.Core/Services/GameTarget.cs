using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

public enum GameTargetKind { Windows, MuMu, LdPlayer, Adb }

public sealed record GameTarget(
    string Id, string Name, GameTargetKind Kind, nint Window, nint RenderWindow,
    string? AdbPath = null, string? Serial = null, string? InstallDir = null,
    int Instance = 0, string? AlternateSerial = null, int VmPid = 0, uint WindowPid = 0)
{
    public const string WindowsId = "windows";

    public bool HasLiveWindow()
    {
        if (!User32.IsWindow(Window)) return false;
        if (WindowPid == 0) return true;
        User32.GetWindowThreadProcessId(Window, out var currentPid);
        return WindowPid == currentPid;
    }

    public RECT? GetRenderRect()
    {
        if (!HasLiveWindow() || User32.IsIconic(Window) || !User32.IsWindowVisible(Window)) return null;
        if (RenderWindow != Window && User32.GetRootWindow(RenderWindow) != Window) return null;
        return User32.GetClientRectScreen(RenderWindow) is { Width: > 0, Height: > 0 } rect ? rect : null;
    }
}

public sealed record GameTargetPreference(
    string Id = GameTarget.WindowsId,
    string? WindowTitle = null, string? ProcessPath = null,
    string? AdbPath = null, string? Serial = null);

public static class GameViewport
{
    public static RECT Fit(RECT bounds, int width, int height)
    {
        if (width <= 0 || height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return bounds;

        double scale = Math.Min((double)bounds.Width / width, (double)bounds.Height / height);
        int fittedWidth = Math.Max(1, (int)Math.Round(width * scale));
        int fittedHeight = Math.Max(1, (int)Math.Round(height * scale));
        int left = bounds.Left + (bounds.Width - fittedWidth) / 2;
        int top = bounds.Top + (bounds.Height - fittedHeight) / 2;
        return new RECT { Left = left, Top = top, Right = left + fittedWidth, Bottom = top + fittedHeight };
    }
}
