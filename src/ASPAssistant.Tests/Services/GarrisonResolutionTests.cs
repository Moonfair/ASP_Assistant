using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public class GarrisonResolutionTests
{
    [Fact]
    public async Task ReadsOverviewWhenOcrMergesTheRosterWarningIntoTheCoreHeading()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog);
        using var input = new MemoryStream(PreparationScannerTests.Frame("enemy-overview-reported.png"));
        using var source = new Bitmap(input);
        using var screen = new Bitmap(1176, 664);
        using (var g = Graphics.FromImage(screen))
        {
            g.Clear(Color.FromArgb(25, 29, 31));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 1, 1176, 662), new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);
        }
        await scanner.ProcessAsync(GarrisonFrameGeometry.Encode(screen));
        Assert.Equal(PreparationPhase.CovenantOverview, scanner.Phase);
        Assert.Equal(11, scanner.CovenantAvailability?.EnabledAdditional.Count);
    }

    [Theory]
    [InlineData("enemy-overview-reported.png", PreparationPhase.CovenantOverview, 11, 3, false, "“萨米的意志”")]
    [InlineData("preparation-reported-speed.png", PreparationPhase.Information, 8, 1, false, "“萨米的意志”")]
    [InlineData("preparation-reported-speed.png", PreparationPhase.Information, 8, 1, true, "“萨米的意志”")]
    [InlineData("preparation-overview.png", PreparationPhase.Information, 11, 2, false, "阿利斯泰尔，帝国余晖")]
    public async Task ReadsCovenantsAndEnemyFromSmallCenteredPreparationPanels(
        string file, PreparationPhase phase, int additionalCount, int battlefield, bool texturedMargins, string boss)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog,
            enemyRecognizer: new PreparationEnemyRecognizer(ocr, Path.Combine(data, "enemy-maps")));
        using var input = new MemoryStream(PreparationScannerTests.Frame(file));
        using var source = new Bitmap(input);
        using var screen = new Bitmap(720, 480);
        using (var g = Graphics.FromImage(screen))
        {
            g.Clear(Color.FromArgb(25, 29, 31));
            if (texturedMargins)
                g.DrawImage(source, new Rectangle(Point.Empty, screen.Size), new Rectangle(60, 170, 150, 120), GraphicsUnit.Pixel);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 37, 720, 405), new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);
        }
        await scanner.ProcessAsync(GarrisonFrameGeometry.Encode(screen));
        Assert.Equal(phase, scanner.Phase);
        Assert.Equal(additionalCount, scanner.CovenantAvailability?.EnabledAdditional.Count);
        Assert.Equal(battlefield, scanner.EnemyInfo?.Battlefield);
        Assert.Equal(boss, scanner.EnemyInfo?.Boss);
    }

    [Fact]
    public async Task KeepsMarkersOnTheCardsInsideALetterboxedClient()
    {
        var names = new[] { "盟约之币", "陨星", "卡达", "角峰" };
        using var input = new MemoryStream(PreparationScannerTests.Frame("shop-default-live.png"));
        using var original = new Bitmap(input);
        using var padded = new Bitmap(1280, 960);
        using (var graphics = Graphics.FromImage(padded))
        {
            graphics.Clear(Color.Black);
            graphics.DrawImage(original, new Rectangle(0, 120, 1280, 720), new Rectangle(Point.Empty, original.Size), GraphicsUnit.Pixel);
        }
        using var ocr = new MaaOcrEngine(Path.Combine(AppContext.BaseDirectory, "data"));
        var shop = new ShopRecognizer(ocr, names);
        var region = new GarrisonOcrStrategy().GetScanRegions().Single(r => r.Name == "Shop");
        var native = await shop.RecognizeAsync(input.ToArray(), 1280, 720, names, region);
        var cards = await shop.RecognizeAsync(GarrisonFrameGeometry.Encode(padded), 1280, 960, names, region);

        Assert.Equal(names, cards.Select(c => c.Name));
        Assert.Equal(native.Count, cards.Count);
        Assert.Equal(native.Select(c => c.Id), cards.Select(c => c.Id));
        var stabilizer = new ShopStateStabilizer();
        Assert.True(stabilizer.TryUpdate(native, out _));
        Assert.True(stabilizer.TryUpdate(cards, out _));
        Assert.False(stabilizer.TryUpdate(cards, out _));
        foreach (var (expected, card) in native.Zip(cards))
            Assert.Equal(120 + expected.OcrRegion.Y * 720, card.OcrRegion.Y * 960, 2);
    }

    [Theory]
    [InlineData("shop-closure-4x3.png", 4, 5, "随身身份牌/地灵/艾丝黛尔/艾丝黛尔")]
    [InlineData("shop-orange-live.png", 4, 5, "维式重锤/褐果/深靛/深靛")]
    [InlineData("shop-default-live.png", 6, 1, "盟约之币/陨星/卡达/角峰")]
    public async Task ReadsNativeClientAndKeepsAllFourCardsAfterResolutionChanges(
        string file, int gold, int upgradeCost, string expectedNames)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var store = new JsonDataStore(data);
        var names = (await store.LoadOperatorsAsync()).Select(op => op.Name)
            .Concat((await store.LoadEquipmentAsync()).Equipment.Select(item => item.Name)).ToArray();
        using var ocr = new MaaOcrEngine(data);
        var recognizer = new GarrisonHudRecognizer(ocr);
        var shop = new ShopRecognizer(ocr, names);
        var region = new GarrisonOcrStrategy().GetScanRegions().Single(r => r.Name == "Shop");
        var native = PreparationScannerTests.Frame(file);
        using var stream = new MemoryStream(native);
        using var original = new Bitmap(stream);
        foreach (var (width, height) in new[] { (original.Width, original.Height), (720, 480), (1280, 800),
                     (800, 600), (1920, 1080), (1280, 960) })
        {
            var png = (width, height) == (original.Width, original.Height) ? native : Reflow(original, width, height);
            var (hud, voice) = await recognizer.RecognizeWithVoiceAsync(png, true);
            Assert.Equal(new GarrisonHud(1, 1, gold, upgradeCost), hud);
            Assert.True(voice.Preparation);
            var cards = await shop.RecognizeAsync(png, width, height, names, region, shopVisible: true);
            Assert.Equal(expectedNames.Split('/'), cards.Select(c => c.Name));
            Assert.Equal(4, cards.Select(c => c.Id).Distinct().Count());
            Assert.All(cards, card => Assert.InRange(card.OcrRegion.Width, .12, .14));
        }
    }

    // Reflow the top HUD and bottom shop independently. Stretching the entire screenshot
    // would distort the glyphs and miss the reported native 4:3 layout.
    private static byte[] Reflow(Bitmap source, int width, int height)
    {
        double scale = Math.Min(width / 1280d, height / 720d);
        int top = (int)Math.Round(180 * scale), bottom = (int)Math.Round(300 * scale);
        using var screen = new Bitmap(width, height);
        using (var g = Graphics.FromImage(screen))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);
            g.DrawImage(source, new Rectangle(0, 0, width, top), new Rectangle(0, 0, 1280, 180), GraphicsUnit.Pixel);
            g.DrawImage(source, new Rectangle(0, height - bottom, width, bottom),
                new Rectangle(0, source.Height - 300, 1280, 300), GraphicsUnit.Pixel);
        }
        using var output = new MemoryStream();
        screen.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
