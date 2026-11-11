using System.Runtime.InteropServices;
using System.Text;

namespace ASPAssistant.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public static class User32
{
    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    public static nint GetRootWindow(nint window) => GetAncestor(window, 2);

    public static IReadOnlyList<(nint Handle, string Title, uint ProcessId)> GetTopLevelWindows()
    {
        var windows = new List<(nint, string, uint)>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            var title = new StringBuilder(512);
            if (GetWindowText(window, title, title.Capacity) == 0) return true;
            GetWindowThreadProcessId(window, out uint processId);
            if (processId != Environment.ProcessId)
                windows.Add((window, title.ToString(), processId));
            return true;
        }, 0);
        return windows;
    }

    internal static nint FindRenderChild(nint window, RECT bounds, int width, int height)
    {
        double aspect = (double)width / height;
        int largestArea = 0;
        nint best = 0;
        EnumChildWindows(window, (child, _) =>
        {
            if (!IsWindowVisible(child) || GetClientRectScreen(child) is not { Width: > 0, Height: > 0 } rect ||
                rect.Width < bounds.Width / 2 || rect.Height < bounds.Height / 2 ||
                rect.Left < bounds.Left || rect.Right > bounds.Right || rect.Top < bounds.Top || rect.Bottom > bounds.Bottom ||
                Math.Abs(rect.Width / (double)rect.Height / aspect - 1) > 0.04)
                return true;
            int area = rect.Width * rect.Height;
            if (area > largestArea)
            {
                largestArea = area;
                best = child;
            }
            return true;
        }, 0);
        return best;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    public const uint PW_RENDERFULLCONTENT = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    public const uint WM_MOUSEWHEEL = 0x020A;

    public static IntPtr FindArknightsWindow()
    {
        return FindWindow(null, "明日方舟");
    }

    public static RECT? GetClientRectScreen(IntPtr hWnd)
    {
        if (!GetClientRect(hWnd, out var clientRect))
            return null;

        var topLeft = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref topLeft))
            return null;

        return new RECT
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = topLeft.X + clientRect.Width,
            Bottom = topLeft.Y + clientRect.Height
        };
    }

    // Monitor APIs

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int    cbSize;
        public RECT   rcMonitor;
        public RECT   rcWork;
        public uint   dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MonitorDefaultToNearest = 2;

    /// <summary>
    /// Returns the bounding rectangle (in physical pixels) of the monitor
    /// that contains the given physical-pixel point, or the nearest monitor
    /// if the point lies outside all monitors.
    /// </summary>
    public static RECT GetMonitorRect(int x, int y)
    {
        var pt      = new POINT { X = x, Y = y };
        var hMon    = MonitorFromPoint(pt, MonitorDefaultToNearest);
        var info    = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMon, ref info);
        return info.rcMonitor;
    }
}
