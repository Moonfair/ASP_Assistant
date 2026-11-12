using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class CovenantOverviewTests
{
    [Theory]
    [InlineData("covenant-overview-inset.png", false, "炎、萨尔贡、阿戈尔、叙拉古、莱塔尼亚", "精准、助力、奇迹、突袭、聚合")]
    [InlineData("covenant-overview-wide.png", true, "谢拉格、阿戈尔、卡西米尔、乌萨斯、伊比利亚", "精准、迅捷、坚守、助力、不屈")]
    public async Task PublishesBansFromTheInGameOverview(string file, bool force, string disabledCore, string disabledAdditional)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        BanCandidate[] candidates =
        [
            new("核心禁用样本", ["阿戈尔"], [], []),
            new("附加禁用样本", [], ["精准"], []),
            new("可用样本", ["维多利亚"], [], [])
        ];
        var scanner = new PreparationScanner(ocr, candidates, covenantCatalog: catalog);

        await scanner.ProcessAsync(PreparationScannerTests.Frame(file), force);

        scanner.CovenantAvailability.Should().NotBeNull();
        scanner.CovenantAvailability!.EnabledCore.Should().BeEquivalentTo(catalog.CoreCovenants.Except(disabledCore.Split('、')));
        scanner.CovenantAvailability.EnabledAdditional.Should().BeEquivalentTo(catalog.AdditionalCovenants.Except(disabledAdditional.Split('、')));
        scanner.BannedNames.Should().BeEquivalentTo("核心禁用样本", "附加禁用样本");
    }

    [Fact]
    public async Task IncompleteOverviewPreservesTheLastConfirmedResult()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog);
        await scanner.ProcessAsync(PreparationScannerTests.Frame("covenant-overview-inset.png"));
        var confirmed = scanner.CovenantAvailability;
        confirmed.Should().NotBeNull();

        using var source = new MemoryStream(PreparationScannerTests.Frame("covenant-overview-wide.png"));
        using var image = new Bitmap(source);
        using (var graphics = Graphics.FromImage(image))
            graphics.FillRectangle(Brushes.Black, 0, 525, 890, 140);
        using var incomplete = new MemoryStream();
        image.Save(incomplete, ImageFormat.Png);
        await scanner.ProcessAsync(incomplete.ToArray(), force: true);

        scanner.Phase.Should().Be(PreparationPhase.CovenantOverview);
        scanner.CovenantAvailability.Should().BeSameAs(confirmed);
    }
}
