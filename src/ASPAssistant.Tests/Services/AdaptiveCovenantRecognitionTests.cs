using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class AdaptiveCovenantRecognitionTests
{
    [Fact]
    public async Task RecognizesTheReportedSpeedModeCoreSelectionAndAdditionalBans()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog);

        await scanner.ProcessAsync(PreparationScannerTests.Frame("preparation-reported-speed.png"));

        scanner.Phase.Should().Be(PreparationPhase.Information);
        scanner.CovenantAvailability.Should().NotBeNull(scanner.Status);
        scanner.CovenantAvailability!.EnabledCore.Should()
            .BeEquivalentTo("莱塔尼亚", "叙拉古", "萨尔贡", "谢拉格");
        scanner.CovenantAvailability.EnabledAdditional.Should()
            .BeEquivalentTo("精准", "迅捷", "助力", "远见", "不屈", "调和", "协防干员", "奥术");
    }

    [Theory]
    [InlineData(1280, 960, 110, 200, 974)]
    [InlineData(1280, 800, 100, 160, 974)]
    [InlineData(800, 600, 30, 150, 720)]
    [InlineData(720, 480, 0, 60, 720)]
    public async Task ReadsTheReportedLayoutAfterScalingAndMovingItWithinTheCapture(
        int width, int height, int left, int top, int gameWidth)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog);
        var png = MoveGame(width, height, left, top, gameWidth);

        await scanner.ProcessAsync(png);

        scanner.Phase.Should().Be(PreparationPhase.Information);
        AssertCovenants(scanner, catalog);
    }

    [Fact]
    public async Task AnIncompleteCoreRowCannotBeReplacedByTheAdditionalRow()
    {
        var ocr = new CoreCovenantBanTests.CoreOcr();
        var candidates = CoreCovenantBanTests.Names.Select(name => new BanCandidate(name, [name], [], [])).ToArray();
        var scanner = new PreparationScanner(ocr, candidates);
        await scanner.ProcessAsync(CoreCovenantBanTests.Frame);
        var confirmed = scanner.CovenantAvailability;
        confirmed.Should().NotBeNull();

        ocr.Row = [CoreCovenantBanTests.Labels[0]];
        using var input = new MemoryStream(CoreCovenantBanTests.Frame);
        using var image = new Bitmap(input);
        using (var graphics = Graphics.FromImage(image))
            graphics.FillRectangle(Brushes.Black, 20, 190, 165, 85);
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        await scanner.ProcessAsync(output.ToArray(), force: true);

        scanner.CovenantAvailability.Should().BeSameAs(confirmed);
        scanner.BannedNames.Should().BeEquivalentTo(CoreCovenantBanTests.Disabled);
    }

    [Fact]
    public async Task RecognizesAFullFrameHeadingSplitAcrossMultipleOcrBoxes()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var native = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(new CountingOcr(native, splitHeading: true), [], covenantCatalog: catalog);

        await scanner.ProcessAsync(MoveGame(1280, 960, 110, 200, 974));

        scanner.Phase.Should().Be(PreparationPhase.Information);
        AssertCovenants(scanner, catalog);
    }

    [Theory]
    [InlineData(1420, 1065, 70, 220, 1280, false)]
    [InlineData(1600, 900, 600, 180, 974, true)]
    [InlineData(2400, 1200, 1600, 220, 800, true)]
    public async Task ReadsBothGroupsAndAdditionalRowsInADisplacedGroupedPreparation(
        int width, int height, int left, int top, int gameWidth, bool rightGroup)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog);

        await scanner.ProcessAsync(MoveGame(width, height, left, top, gameWidth, "preparation-grouped-scrolled.png", rightGroup));

        scanner.Phase.Should().Be(PreparationPhase.Information);
        scanner.CovenantAvailability.Should().NotBeNull(scanner.Status);
        scanner.CovenantAvailability!.EnabledCore.Should().BeEquivalentTo(rightGroup
            ? new[] { "乌萨斯", "维多利亚", "萨尔贡", "谢拉格", "阿戈尔" }
            : new[] { "莱塔尼亚", "卡西米尔", "炎", "伊比利亚", "叙拉古" });
        catalog.ModePools["规程等级7"].AdditionalCovenants.Except(scanner.CovenantAvailability.EnabledAdditional)
            .Should().BeEquivalentTo("迅捷", "灵巧", "奇迹", "突袭", "聚合");

        await scanner.ProcessAsync(MoveGame(width, height, left, top, gameWidth, "preparation-grouped-scrolled.png", !rightGroup));
        scanner.CovenantAvailability!.EnabledCore.Should().BeEquivalentTo(rightGroup
            ? new[] { "莱塔尼亚", "卡西米尔", "炎", "伊比利亚", "叙拉古" }
            : new[] { "乌萨斯", "维多利亚", "萨尔贡", "谢拉格", "阿戈尔" });
    }

    [Fact]
    public async Task ReusesDiscoveryAndRecalibratesAfterTheSameSizeCaptureMoves()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var native = new MaaOcrEngine(data);
        var ocr = new CountingOcr(native);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog);
        var first = MoveGame(1280, 960, 110, 200, 974);

        await scanner.ProcessAsync(first);
        AssertCovenants(scanner, catalog);
        ocr.FullScans.Should().Be(1);
        await scanner.ProcessAsync(first);
        AssertCovenants(scanner, catalog);
        ocr.FullScans.Should().Be(1);

        await scanner.ProcessAsync(MoveGame(1280, 960, 50, 310, 974));
        AssertCovenants(scanner, catalog);
        ocr.FullScans.Should().Be(2);
    }

    [Fact]
    public async Task AnObscuredGrayIconPreservesTheLastConfirmedResultAndTargetResetClearsIt()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog);
        var first = MoveGame(1280, 960, 110, 200, 974);
        await scanner.ProcessAsync(first);
        var confirmed = scanner.CovenantAvailability;
        confirmed.Should().NotBeNull(scanner.Status);

        using var input = new MemoryStream(first);
        using var image = new Bitmap(input);
        using (var graphics = Graphics.FromImage(image))
            graphics.FillRectangle(Brushes.Black, 465, 335, 60, 60);
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        await scanner.ProcessAsync(output.ToArray(), force: true);
        scanner.CovenantAvailability.Should().BeSameAs(confirmed);
        scanner.Status.Should().Contain("识别不完整");

        scanner.ResetTarget();
        scanner.CovenantAvailability.Should().BeNull();
        await scanner.ProcessAsync(MoveGame(800, 600, 30, 150, 720));
        AssertCovenants(scanner, catalog);
    }

    internal static byte[] MoveGame(int width, int height, int left, int top, int gameWidth,
        string file = "preparation-reported-547p.png", bool rightGroup = false)
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(file));
        using var source = new Bitmap(input);
        if (rightGroup)
        {
            using var bar = source.Clone(new Rectangle(22, 342, 423, 8), PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(source);
            graphics.FillRectangle(Brushes.Black, 22, 342, 423, 8);
            graphics.DrawImage(bar, new Rectangle(453, 342, 423, 8),
                new Rectangle(Point.Empty, bar.Size), GraphicsUnit.Pixel);
        }
        using var image = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(left, top, gameWidth,
                (int)Math.Round(source.Height * (double)gameWidth / source.Width)),
                new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);
        }
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    internal static void AssertCovenants(PreparationScanner scanner, CovenantCatalog catalog)
    {
        scanner.CovenantAvailability.Should().NotBeNull(scanner.Status);
        var pool = catalog.ModePools["终极模拟"];
        pool.CoreCovenants.Except(scanner.CovenantAvailability!.EnabledCore).Should()
            .BeEquivalentTo("维多利亚", "叙拉古", "乌萨斯", "伊比利亚", "莱塔尼亚");
        pool.AdditionalCovenants.Except(scanner.CovenantAvailability.EnabledAdditional).Should()
            .BeEquivalentTo("灵巧", "坚守", "助力", "突袭", "聚合");
    }

    private sealed class CountingOcr(IOcrEngine inner, bool splitHeading = false) : IOcrEngine
    {
        public int FullScans { get; private set; }
        public async Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height)
        {
            var size = ScreenCaptureService.GetPngSize(pngBytes);
            bool full = x == 0 && y == 0 && width == size.Width && height == size.Height;
            if (full) FullScans++;
            var texts = await inner.RecognizeRegionAsync(pngBytes, x, y, width, height);
            if (!splitHeading || !full) return texts;
            return texts.SelectMany(text => text.Text.Contains("确认本局信息")
                ? new[] { text with { Text = "确认本局" }, text with { Text = "信息" } }
                : new[] { text }).ToArray();
        }
    }
}
