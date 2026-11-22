using System.Drawing;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class PreparationEnemyTests
{
    [Fact]
    public async Task NotifiesTheNextGameEvenWhenThePreviousEnemiesWereNeverRecognized()
    {
        var ocr = new EnemyPanelOcr { Title = "确定自选编队", PanelVisible = false };
        var recognizer = new PreparationEnemyRecognizer(ocr, Path.Combine(AppContext.BaseDirectory, "data", "enemy-maps"));
        var scanner = new PreparationScanner(ocr, [], enemyRecognizer: recognizer);
        var changes = new List<PreparationEnemyInfo?>();
        scanner.EnemyInfoChanged += changes.Add;
        var png = PreparationScannerTests.Frame("preparation-reported-incomplete.png");

        await scanner.ProcessAsync(png);
        scanner.EnemyInfo.Should().BeNull();
        changes.Should().BeEmpty();

        ocr.Title = "确认本局信息";
        await scanner.ProcessAsync(png);
        changes.Should().ContainSingle().Which.Should().BeNull("the view must clear its manual selections for the new game");

        await scanner.ProcessAsync(png);
        changes.Should().ContainSingle("remaining on the same preparation page must preserve manual selections");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsEnemyInformationFromTheReportedCovenantOverview(bool force)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var recognizer = new PreparationEnemyRecognizer(ocr, Path.Combine(data, "enemy-maps"));
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog, enemyRecognizer: recognizer);

        await scanner.ProcessAsync(PreparationScannerTests.Frame("enemy-overview-reported.png"), force);

        scanner.Phase.Should().Be(PreparationPhase.CovenantOverview);
        scanner.EnemyInfo.Should().NotBeNull();
        scanner.EnemyInfo!.Boss.Should().Be("“萨米的意志”");
        scanner.EnemyInfo.EnemyTypes.Should().Equal("频次", "折射", "致命");
    }

    [Fact]
    public async Task RecognizesTheReportedOverviewEnemyPanel()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        using var ocr = new MaaOcrEngine(data);
        var recognizer = new PreparationEnemyRecognizer(ocr, Path.Combine(data, "enemy-maps"));
        var png = PreparationScannerTests.Frame("enemy-overview-reported.png");
        using var stream = new MemoryStream(png);
        using var image = new Bitmap(stream);

        var info = await recognizer.ReadAsync(png, image, null, null);

        info.Should().NotBeNull();
        info!.Boss.Should().Be("“萨米的意志”");
        info.EnemyTypes.Should().Equal("频次", "折射", "致命");
    }

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

    [Fact]
    public async Task CompletesEnemyInformationInTheOverviewWithoutLosingTheKnownMode()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        var ocr = new EnemyPanelOcr { Title = "确认本局信息 终极模拟", MissingLastType = true };
        var recognizer = new PreparationEnemyRecognizer(ocr, Path.Combine(data, "enemy-maps"));
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog, enemyRecognizer: recognizer);
        await scanner.ProcessAsync(PreparationScannerTests.Frame("preparation-reported-incomplete.png"));
        scanner.EnemyInfo!.EnemyTypes.Should().Equal("致命", "复苏");
        scanner.EnemyInfo.Mode.Should().Be("终极模拟");

        ocr.Title = "STRONGHOLD PROTOCOL";
        ocr.MissingLastType = false;
        await scanner.ProcessAsync(PreparationScannerTests.Frame("enemy-overview-reported.png"));

        scanner.Phase.Should().Be(PreparationPhase.CovenantOverview);
        scanner.EnemyInfo!.EnemyTypes.Should().Equal("致命", "复苏", "折射");
        scanner.EnemyInfo.Mode.Should().Be("终极模拟");
        scanner.EnemyInfo.Battlefield.Should().Be(6);
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
        public bool MissingLastType { get; set; }

        public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height)
        {
            IReadOnlyList<OcrTextResult> results = y == 0
                ? [new(Title, (40, 20, 300, 30))]
                : x > 900 && PanelVisible
                    ? [new("敌方领袖", (1065, 334, 71, 22)), new("假想敌：统", (1032, 366, 121, 25)),
                       new("特训", (1034, 456, 48, 28)), new("致命", (1123, 456, 48, 27)),
                       new("特训敌人·复苏", (1036, 507, 133, 25)), new(MissingLastType ? "" : "特训敌人·折射", (1037, 558, 131, 22))]
                    : x == 0 && Title == "STRONGHOLD PROTOCOL"
                        ? [new("核心盟约", (74, 132, 100, 25)), new("附加盟约", (74, 343, 100, 25))]
                    : [];
            return Task.FromResult(results);
        }
    }
}
