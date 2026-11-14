using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public sealed class GarrisonHudRecognizerTests : IDisposable
{
    private readonly MaaOcrEngine _ocr = new(Path.Combine(AppContext.BaseDirectory, "data"));

    [Theory]
    [InlineData("shop-new-skin.png", 1280)]
    [InlineData("shop-new-skin.png", 960)]
    [InlineData("shop-classic.png", 819)]
    [InlineData("shop-light-theme.png", 1280)]
    [InlineData("shop-light-theme.png", 960)]
    [InlineData("bob-closure-rest.png", 1280)]
    [InlineData("bob-closure-rest.png", 960)]
    public async Task ReadsHudAcrossThemesAndResolutions(string file, int width)
    {
        using var source = LoadGameFrame(file);
        using var resized = new Bitmap(source, width, source.Height * width / source.Width);
        var result = await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(resized));

        Assert.Equal(new GarrisonHud(1, 1, 4, 5), result);
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1440, 720)]
    public async Task LocatesHudInsidePaddedEmulatorCapture(int width, int height)
    {
        using var source = LoadGameFrame("shop-new-skin.png");
        using var padded = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(padded))
        {
            graphics.Clear(Color.Black);
            graphics.DrawImageUnscaled(source, (width - source.Width) / 2, (height - source.Height) / 2);
        }

        Assert.Equal(new GarrisonHud(1, 1, 4, 5),
            await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(padded)));
    }

    [Fact]
    public async Task RealMaaHudFrameTriggersBobOnlyAfterConfirmation()
    {
        var recognizer = new GarrisonHudRecognizer(_ocr);
        var triggers = new BobVoiceTriggers();
        using var source = LoadGameFrame("shop-new-skin.png");
        var png = Encode(source);

        Assert.Null(triggers.Observe(await recognizer.RecognizeAsync(png), []));
        Assert.Equal(BobVoiceCue.Opening, triggers.Observe(await recognizer.RecognizeAsync(png), []));
        Assert.Null(triggers.Observe(await recognizer.RecognizeAsync(png), []));
    }

    [Fact]
    public async Task DoesNotReadPreparationScreenAsInMatchHud()
    {
        var png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "preparation-standard.png"));

        Assert.Equal(default, await new GarrisonHudRecognizer(_ocr).RecognizeAsync(png));
    }

    [Fact]
    public async Task IgnoresTruncatedCaptureEvenWithAValidPngHeader()
    {
        using var frame = new Bitmap(960, 540);
        var header = Encode(frame)[..33];

        Assert.Equal(default, await new GarrisonHudRecognizer(_ocr).RecognizeAsync(header));
    }

    [Fact]
    public async Task KeepsRoundWhenShopIsObscured()
    {
        using var source = LoadGameFrame("shop-new-skin.png");
        using (var graphics = Graphics.FromImage(source))
            graphics.FillRectangle(Brushes.Black, 0, 420, source.Width, source.Height - 420);

        Assert.Equal(new GarrisonHud(1, null, null),
            await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(source)));
    }

    [Fact]
    public async Task RejectsLevelLabelTooCloseToTheBottomWithoutLosingOtherFields()
    {
        using var source = LoadGameFrame("shop-new-skin.png");
        using var label = source.Clone(new Rectangle(349, 541, 77, 16), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.FillRectangle(Brushes.Black, 330, 540, 110, 175);
            graphics.DrawImageUnscaled(label, 349, 650);
        }

        Assert.Equal(new GarrisonHud(1, null, 4),
            await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(source)));
    }

    [Fact]
    public async Task ShopArtworkAloneDoesNotSupplyLiveValues()
    {
        using var source = LoadGameFrame("shop-new-skin.png");
        using (var graphics = Graphics.FromImage(source))
            graphics.FillRectangle(Brushes.Black, 0, 0, source.Width, 80);

        Assert.Equal(default, await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(source)));
    }

    [Fact]
    public async Task ReadsMultipleDigitsBesideTheAnchors()
    {
        using var source = LoadGameFrame("shop-new-skin.png");
        using (var graphics = Graphics.FromImage(source))
        {
            using var roundBackground = new SolidBrush(Color.FromArgb(192, 218, 207));
            graphics.FillRectangle(roundBackground, 462, 25, 41, 16);
            using var roundFont = new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString("12", roundFont, Brushes.Black, 474, 23);

            using var goldBackground = new SolidBrush(Color.FromArgb(24, 19, 12));
            graphics.FillRectangle(goldBackground, 1184, 519, 38, 17);
            using var goldFont = new Font("Arial", 19, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString("104", goldFont, Brushes.Gold, 1185, 516);
        }

        Assert.Equal(new GarrisonHud(12, 1, 104, 5),
            await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(source)));
    }

    [Fact]
    public async Task ReadsZeroGoldAndCountsRepeatedLevelStrokes()
    {
        using var source = LoadGameFrame("shop-new-skin.png");
        using var stroke = source.Clone(new Rectangle(378, 583, 18, 27), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            using var goldBackground = new SolidBrush(Color.FromArgb(24, 19, 12));
            graphics.FillRectangle(goldBackground, 1184, 519, 38, 17);
            using var goldFont = new Font("Arial", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString("0", goldFont, Brushes.Gold, 1191, 516);

            using var levelBackground = new SolidBrush(Color.FromArgb(75, 75, 75));
            graphics.FillRectangle(levelBackground, 360, 570, 53, 50);
            graphics.DrawImageUnscaled(stroke, 370, 583);
            graphics.DrawImageUnscaled(stroke, 390, 583);
        }

        Assert.Equal(new GarrisonHud(1, 2, 0, 5),
            await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(source)));
    }

    [Fact]
    public async Task ReadsDarkRomanStrokesAndChangedUpgradeCost()
    {
        using var source = LoadGameFrame("shop-light-theme.png");
        using var stroke = source.Clone(new Rectangle(385, 576, 10, 24), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            using var levelBackground = new SolidBrush(Color.FromArgb(221, 221, 221));
            graphics.FillRectangle(levelBackground, 376, 576, 29, 26);
            graphics.DrawImageUnscaled(stroke, 377, 576);
            graphics.DrawImageUnscaled(stroke, 394, 576);

            using var costBackground = new SolidBrush(Color.FromArgb(66, 66, 66));
            graphics.FillRectangle(costBackground, 380, 523, 18, 20);
            using var costFont = new Font("Arial", 17, FontStyle.Regular, GraphicsUnit.Pixel);
            graphics.DrawString("12", costFont, Brushes.White, 379, 523);
        }

        Assert.Equal(new GarrisonHud(1, 2, 4, 12),
            await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(source)));
    }

    [Fact]
    public async Task KeepsLevelAndGoldWhenUpgradeBadgeIsAbsent()
    {
        using var source = LoadGameFrame("shop-light-theme.png");
        using (var graphics = Graphics.FromImage(source))
            graphics.FillRectangle(Brushes.Black, 362, 509, 56, 44);

        Assert.Equal(new GarrisonHud(1, 1, 4, null),
            await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(source)));
    }

    [Theory]
    [InlineData(4, 0, 372)]
    [InlineData(5, -4, null)]
    [InlineData(6, -12, 399)]
    public async Task ReadsFadingRomanLevelsWithAConvergingPair(int level, int vOffset, int? strokeX)
    {
        using var source = LoadGameFrame("shop-light-theme.png");
        using var stroke = source.Clone(new Rectangle(385, 576, 10, 24), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            using var background = new SolidBrush(Color.FromArgb(221, 221, 221));
            graphics.FillRectangle(background, 370, 576, 42, 26);
            if (strokeX is { } x)
                graphics.DrawImageUnscaled(stroke, x, 576);
            graphics.TranslateTransform(vOffset, 0);
            using var ink = new LinearGradientBrush(new Rectangle(385, 576, 25, 25),
                Color.FromArgb(12, 12, 12), Color.FromArgb(184, 184, 184), LinearGradientMode.Vertical);
            graphics.FillPolygon(ink, new Point[]
            {
                new(386, 576), new(391, 576), new(397, 593), new(403, 576),
                new(408, 576), new(400, 601), new(394, 601)
            });
        }

        Assert.Equal(new GarrisonHud(1, level, 4, 5),
            await new GarrisonHudRecognizer(_ocr).RecognizeAsync(Encode(source)));
    }

    private static Bitmap LoadGameFrame(string file)
    {
        using var source = new Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var bounds = file == "shop-classic.png"
            ? new Rectangle(1, 20, 819, 461)
            : new Rectangle(Point.Empty, source.Size);
        return source.Clone(bounds, PixelFormat.Format32bppArgb);
    }

    private static byte[] Encode(Bitmap image)
    {
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    public void Dispose() => _ocr.Dispose();
}
