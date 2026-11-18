using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

internal static class GarrisonFrameGeometry
{
    internal static double UiScale(int width, int height) => Math.Min(width / 1280d, height / 720d);

    // Some clients letterbox, others render the board to both edges at the same aspect ratio.
    // Check both margins and the transition to content before dropping any pixels.
    internal static Rectangle Viewport(Bitmap image)
    {
        var full = new Rectangle(Point.Empty, image.Size);
        var fit = GameViewport.Fit(new RECT { Right = image.Width, Bottom = image.Height }, 1280, 720);
        if ((fit.Top < 4 && fit.Left < 4) || fit.Width < 8 || fit.Height < 8) return full;
        bool horizontal = fit.Top >= 4;
        int first = horizontal ? fit.Top : fit.Left, last = horizontal ? fit.Bottom : fit.Right;
        var margin = horizontal ? image.GetPixel(image.Width / 2, first - 2) : image.GetPixel(first - 2, image.Height / 2);
        bool IsMargin(int index)
        {
            int length = horizontal ? image.Width : image.Height;
            for (int i = 1; i < 16; i++)
            {
                int p = length * i / 16;
                var c = horizontal ? image.GetPixel(p, index) : image.GetPixel(index, p);
                if (Math.Abs(c.R - margin.R) > 12 || Math.Abs(c.G - margin.G) > 12 || Math.Abs(c.B - margin.B) > 12) return false;
            }
            return true;
        }
        return IsMargin(first / 2) && IsMargin(first - 2) && IsMargin(last + 1) &&
               IsMargin(last + first / 2 - 1) && (!IsMargin(first + 2) || !IsMargin(last - 3))
            ? Rectangle.FromLTRB(fit.Left, fit.Top, fit.Right, fit.Bottom) : full;
    }

    internal static Bitmap Resize(Bitmap source, Rectangle area, Rectangle bounds)
    {
        if (area.Size == bounds.Size) return source.Clone(area, PixelFormat.Format24bppRgb);
        var resized = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(resized);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, bounds, area, GraphicsUnit.Pixel);
        return resized;
    }

    internal static byte[] Encode(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
