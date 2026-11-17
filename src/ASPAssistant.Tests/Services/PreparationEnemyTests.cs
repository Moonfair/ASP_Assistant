using System.Drawing;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class PreparationEnemyTests
{
    [Fact]
    public async Task RetainsEnemyInformationThroughSquadAndClearsItOnNextPreparation()
    {
        var ocr = new EnemyPanelOcr();
        var recognizer = new PreparationEnemyRecognizer(ocr, Path.Combine(AppContext.BaseDirectory, "data", "enemy-maps"));
        var scanner = new PreparationScanner(ocr, [], enemyRecognizer: recognizer);
        var png = PreparationScannerTests.Frame("preparation-reported-incomplete.png");

        await scanner.ProcessAsync(png);
        scanner.EnemyInfo!.Boss.Should().Be("假想敌：铳");
        scanner.EnemyInfo.Battlefield.Should().Be(6);
        scanner.EnemyInfo.EnemyTypes.Should().Equal("致命", "复苏", "折射");

        ocr.Title = "确定自选编队";
        await scanner.ProcessAsync(png);
        scanner.EnemyInfo!.Boss.Should().Be("假想敌：铳");

        ocr.Title = "确认本局信息";
        ocr.PanelVisible = false;
        await scanner.ProcessAsync(PreparationScannerTests.Frame("preparation-standard.png"));
        scanner.EnemyInfo.Should().BeNull();
    }

    [Theory]
    [InlineData("preparation-overview.png", 2)]
    [InlineData("preparation-core-bans.png", 11)]
    [InlineData("preparation-windowed.png", 5)]
    [InlineData("preparation-terminal.png", 10)]
    [InlineData("preparation-standard.png", null)]
    public void IdentifiesBattlefieldFromTheBpPreview(string file, int? battlefield)
    {
        using var stream = new MemoryStream(PreparationScannerTests.Frame(file));
        using var image = new Bitmap(stream);
        var matcher = new PreparationMapMatcher(Path.Combine(AppContext.BaseDirectory, "data", "enemy-maps"));
        matcher.Match(image).Should().Be(battlefield);
    }

    [Theory]
    [InlineData("假想敌：铳", "绝境模拟", 14, true, "06-patrol.jpg", "L1 → L2 → L3 → L1")]
    [InlineData("假想敌：鳍", "终极模拟", 15, false, "06-patrol.jpg", "R1 → R2 → R3 → R1")]
    [InlineData("矩兽", "险境模拟", 14, false, "06-beast.jpg", "R1 → R2 → R3 → R1")]
    public void ResolvesTheRoundAndSide(string boss, string mode, int round, bool left, string file, string order)
    {
        var route = BossRoutes.For(boss, mode, 6, round);
        route!.ImageFile.Should().Be(file);
        route.Order(left).Should().Be(order);
    }

    [Theory]
    [InlineData("矩兽", "终极模拟", 15)]
    [InlineData("假想敌：铳", "标准模拟", 15)]
    [InlineData("假想敌：鳍", "规程等级4", 15)]
    [InlineData("假想敌：铳", "极速模拟", 14)]
    [InlineData("假想敌：胄", "绝境模拟", 14)]
    public void LeavesUnsupportedRoundsOrBossesWithoutAChart(string boss, string mode, int round) =>
        BossRoutes.For(boss, mode, 6, round).Should().BeNull();

    private sealed class EnemyPanelOcr : IOcrEngine
    {
        public string Title { get; set; } = "确认本局信息";
        public bool PanelVisible { get; set; } = true;

        public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height)
        {
            IReadOnlyList<OcrTextResult> results = y == 0
                ? [new(Title, (400, 20, 200, 30))]
                : x > 900 && PanelVisible
                    ? [new("敌方领袖", (1065, 334, 71, 22)), new("假想敌：统", (1032, 366, 121, 25)),
                       new("特训", (1034, 456, 48, 28)), new("致命", (1123, 456, 48, 27)),
                       new("特训敌人·复苏", (1036, 507, 133, 25)), new("特训敌人·折射", (1037, 558, 131, 22))]
                    : [];
            return Task.FromResult(results);
        }
    }
}
