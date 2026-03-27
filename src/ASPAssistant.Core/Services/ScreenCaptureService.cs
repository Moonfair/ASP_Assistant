using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

public class ScreenCaptureService
{
    /// <summary>
    /// Captures the Arknights game window using PrintWindow (works with DirectX/fullscreen).
    /// Returns PNG bytes, or null if the window is not found.
    /// </summary>
    public byte[]? CaptureScreen()
    {
        var hwnd = User32.FindArknightsWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        if (!User32.GetClientRect(hwnd, out var clientRect))
            return null;

        int width = clientRect.Width;
        int height = clientRect.Height;
        if (width <= 0 || height <= 0)
            return null;

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        try
        {
            User32.PrintWindow(hwnd, hdc, User32.PW_RENDERFULLCONTENT);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
