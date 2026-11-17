using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ASPAssistant.Mac;

internal static class MacWindowInteraction
{
    public static void MakeClickThrough(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero) ASPASetClickThrough(handle);
    }

    public static void PositionAtQuartzTopLeft(Window window, double x, double y)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero) ASPASetQuartzTopLeft(handle, x, y);
    }

    public static void PositionBeside(Window window, double left, double top, double right)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero) ASPAPositionBeside(handle, left, top, right);
    }

    [DllImport("libaspa-window.dylib")]
    private static extern void ASPASetClickThrough(nint window);
    [DllImport("libaspa-window.dylib")]
    private static extern void ASPASetQuartzTopLeft(nint window, double x, double y);
    [DllImport("libaspa-window.dylib")]
    private static extern void ASPAPositionBeside(nint window, double left, double top, double right);
}
