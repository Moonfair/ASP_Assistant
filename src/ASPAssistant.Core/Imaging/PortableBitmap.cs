using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ASPAssistant.Core.Imaging;

/// <summary>Small GDI-compatible image surface for the shared recognizers on macOS.</summary>
public sealed class PortableBitmap : IDisposable
{
    internal SKBitmap Pixels { get; }

    public int Width => Pixels.Width;
    public int Height => Pixels.Height;
    public Size Size => new(Width, Height);
    public PixelFormat PixelFormat => PixelFormat.Format32bppArgb;

    public PortableBitmap(Stream stream)
    {
        using var input = SKData.Create(stream);
        using var decoded = SKBitmap.Decode(input) ?? throw new ArgumentException("Invalid image", nameof(stream));
        Pixels = NewSurface(decoded.Width, decoded.Height);
        using var canvas = new SKCanvas(Pixels);
        canvas.DrawBitmap(decoded, 0, 0);
        canvas.Flush();
    }

    public PortableBitmap(int width, int height) : this(width, height, PixelFormat.Format32bppArgb) { }

    public PortableBitmap(int width, int height, PixelFormat format)
    {
        Pixels = NewSurface(width, height);
    }

    public PortableBitmap(PortableBitmap source, int width, int height)
    {
        Pixels = NewSurface(width, height);
        using var canvas = new SKCanvas(Pixels);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.DrawBitmap(source.Pixels, new SKRect(0, 0, width, height), paint);
        canvas.Flush();
    }

    internal PortableBitmap(SKBitmap pixels) => Pixels = pixels;

    private static SKBitmap NewSurface(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    }

    public Color GetPixel(int x, int y)
    {
        var value = Pixels.GetPixel(x, y);
        return Color.FromArgb(value.Alpha, value.Red, value.Green, value.Blue);
    }

    public PortableBitmap Clone(Rectangle region, PixelFormat format)
    {
        if (region.Width <= 0 || region.Height <= 0 ||
            region.Left < 0 || region.Top < 0 || region.Right > Width || region.Bottom > Height)
            throw new ArgumentOutOfRangeException(nameof(region));
        var result = new PortableBitmap(region.Width, region.Height, format);
        using var canvas = new SKCanvas(result.Pixels);
        canvas.DrawBitmap(Pixels,
            new SKRect(region.Left, region.Top, region.Right, region.Bottom),
            new SKRect(0, 0, region.Width, region.Height));
        canvas.Flush();
        return result;
    }

    public void Save(Stream stream, ImageFormat format)
    {
        using var image = SKImage.FromBitmap(Pixels);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        encoded.SaveTo(stream);
    }

    public byte[] CopyBgraPixels()
    {
        var result = new byte[Pixels.RowBytes * Height];
        Marshal.Copy(Pixels.GetPixels(), result, 0, result.Length);
        return result;
    }

    public void Dispose() => Pixels.Dispose();
}

public sealed class PortableGraphics : IDisposable
{
    private readonly SKCanvas _canvas;
    private PortableGraphics(PortableBitmap image) => _canvas = new SKCanvas(image.Pixels);
    public static PortableGraphics FromImage(PortableBitmap image) => new(image);
    public System.Drawing.Drawing2D.InterpolationMode InterpolationMode { get; set; }

    public void Clear(Color color) => _canvas.Clear(new SKColor(color.R, color.G, color.B, color.A));

    public void DrawImage(PortableBitmap source, int x, int y, int width, int height) =>
        DrawImage(source, new Rectangle(x, y, width, height),
            new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);

    public void DrawImage(PortableBitmap source, Rectangle destination, Rectangle origin, GraphicsUnit unit)
    {
        using var paint = new SKPaint
        {
            FilterQuality = InterpolationMode == System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                ? SKFilterQuality.High : SKFilterQuality.Medium,
            IsAntialias = true
        };
        _canvas.DrawBitmap(source.Pixels,
            new SKRect(origin.Left, origin.Top, origin.Right, origin.Bottom),
            new SKRect(destination.Left, destination.Top, destination.Right, destination.Bottom), paint);
    }

    public void Dispose()
    {
        _canvas.Flush();
        _canvas.Dispose();
    }
}
