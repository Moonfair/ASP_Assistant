using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.Imaging;

namespace ASPAssistant.Mac.Tests;

public class PortableBitmapTests
{
    [Fact]
    public void CropResizeAndPngRoundTripPreservePixels()
    {
        using var source = new PortableBitmap(2, 2);
        using (var graphics = PortableGraphics.FromImage(source))
            graphics.Clear(Color.FromArgb(255, 22, 110, 180));
        using var crop = source.Clone(new Rectangle(1, 1, 1, 1), PixelFormat.Format32bppArgb);
        using var resized = new PortableBitmap(crop, 4, 4);
        using var encoded = new MemoryStream();
        resized.Save(encoded, ImageFormat.Png);
        encoded.Position = 0;
        using var decoded = new PortableBitmap(encoded);

        Assert.Equal(new Size(4, 4), decoded.Size);
        Assert.Equal(Color.FromArgb(255, 22, 110, 180), decoded.GetPixel(2, 2));
        var bgra = decoded.CopyBgraPixels();
        Assert.Equal([180, 110, 22, 255], bgra[..4]);
    }
}
