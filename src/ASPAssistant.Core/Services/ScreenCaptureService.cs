using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

public class ScreenCaptureService
{
    /// <summary>
    /// Captures the Arknights game window by reading pixels directly from the screen
    /// via BitBlt (Graphics.CopyFromScreen). This works with hardware-accelerated
    /// renderers (DirectX/Vulkan) that do not honour PrintWindow/GDI capture.
    /// Requires the window to be visible and not fully occluded.
    /// Returns PNG bytes, or null if the window is not found.
    /// </summary>
    public byte[]? CaptureScreen()
    {
        var hwnd = User32.FindArknightsWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        // Skip minimized windows: ClientToScreen returns (-32000,-32000) for them,
        // which causes CopyFromScreen to throw Win32Exception(6) ERROR_INVALID_HANDLE.
        if (User32.IsIconic(hwnd))
            return null;

        var screenRect = User32.GetClientRectScreen(hwnd);
        if (screenRect == null)
            return null;

        var rect = screenRect.Value;
        int width = rect.Width;
        int height = rect.Height;
        if (width <= 0 || height <= 0)
            return null;

        try
        {
            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Window state changed between the geometry query and the capture
            // (e.g. minimized mid-capture). Treat as no screenshot available.
            return null;
        }
    }
}
