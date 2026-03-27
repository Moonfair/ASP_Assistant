using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ASPAssistant.Core.Services;

public class WindowsOcrEngine
{
    private readonly OcrEngine _engine;

    public WindowsOcrEngine()
    {
        // Prefer Simplified Chinese for Arknights operator names
        var zhHans = new Language("zh-Hans");
        _engine = OcrEngine.TryCreateFromLanguage(zhHans)
            ?? OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "No OCR engine available. Install a Chinese language pack or ensure user profile languages include Chinese.");
    }

    /// <summary>
    /// Runs OCR on a sub-region of the supplied PNG byte array.
    /// Coordinates are in pixels relative to the full image.
    /// Returns the recognized text, or empty string on failure.
    /// </summary>
    public async Task<string> RecognizeRegionAsync(
        byte[] pngBytes, int x, int y, int width, int height)
    {
        try
        {
            using var ms = new MemoryStream(pngBytes);
            using var fullBmp = new Bitmap(ms);

            // Clamp region to image bounds
            var imgWidth = fullBmp.Width;
            var imgHeight = fullBmp.Height;
            x = Math.Max(0, Math.Min(x, imgWidth - 1));
            y = Math.Max(0, Math.Min(y, imgHeight - 1));
            width = Math.Max(1, Math.Min(width, imgWidth - x));
            height = Math.Max(1, Math.Min(height, imgHeight - y));

            using var crop = fullBmp.Clone(
                new Rectangle(x, y, width, height),
                PixelFormat.Format32bppArgb);

            var softBmp = BitmapToSoftwareBitmap(crop);
            var result = await _engine.RecognizeAsync(softBmp);
            return result.Text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static SoftwareBitmap BitmapToSoftwareBitmap(Bitmap bmp)
    {
        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        var bytes = new byte[Math.Abs(bmpData.Stride) * bmp.Height];
        Marshal.Copy(bmpData.Scan0, bytes, 0, bytes.Length);
        bmp.UnlockBits(bmpData);

        // WinRT SoftwareBitmap expects Bgra8 (same as GDI Format32bppArgb)
        var buffer = bytes.AsBuffer();
        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            bmp.Width,
            bmp.Height,
            BitmapAlphaMode.Premultiplied);
    }
}
