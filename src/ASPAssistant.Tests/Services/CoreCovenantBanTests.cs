using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class CoreCovenantBanTests
{
    internal static readonly string[] Names = ["炎", "维多利亚", "叙拉古", "卡西米尔", "莱塔尼亚", "萨尔贡", "谢拉格", "阿戈尔", "乌萨斯", "伊比利亚"];
    internal static readonly string[] Disabled = Names[5..];
    internal static IReadOnlyList<BanCandidate> Candidates => [
        .. Names.Select(n => new BanCandidate(n + "干员", [n], [], [])),
        new("双核心", ["莱塔尼亚", "萨尔贡"], ["精准"], []),
        new("三核心", ["卡西米尔", "谢拉格", "莱塔尼亚"], ["精准"], []),
        new("多核心无附加", ["莱塔尼亚", "萨尔贡"], [], []),
        new("仅附加命中", ["炎"], ["精准"], []),
        .. AdditionalCovenantRecognitionTests.Names.Select(n => new BanCandidate("无核心" + n, [], [n], []))];
    internal static byte[] Frame => PreparationScannerTests.Frame("preparation-core-bans.png");

    [Fact]
    public async Task AnyMissingCoreAlsoBansOperatorsWithMultipleCoreCovenants()
    {
        var scanner = new PreparationScanner(new CoreOcr(), Candidates.Where(c => c.AdditionalCovenants.Count == 0).ToList());
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().BeEquivalentTo(Disabled.Select(n => n + "干员").Append("多核心无附加"));
    }

    // Actual native OCR output: neighboring names can be merged into a single box.
    internal static IReadOnlyList<OcrTextResult> Labels => [
        new("核心盟约部分盟约所含干员阵容不完整", (16, 123, 464, 41)),
        new("附加盟约", (72, 343, 101, 27)),
        new("炎", (46, 278, 43, 22)),
        new("维多利亚 叙拉古 卡西米尔 莱塔尼亚 萨尔贡", (106, 276, 445, 26)),
        new("谢拉格阿戈尔", (543, 277, 150, 23)),
        new("乌萨斯伊比利亚", (684, 276, 184, 23)),
        .. AdditionalCovenantRecognitionTests.Texts("preparation-core-bans.png").Where(t => t.Box.Y > 400)];

    [Fact]
    public async Task OverviewAppliesTheTwoCovenantRules()
    {
        var scanner = new PreparationScanner(new CoreOcr(), Candidates);
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().BeEquivalentTo(Disabled.Select(n => n + "干员").Concat([
            "双核心", "三核心", "多核心无附加", "无核心精准", "无核心迅捷", "无核心助力", "无核心远见", "无核心奥术"]));
        scanner.BannedNames.Should().NotContain("仅附加命中");
        scanner.Status.Should().Contain("萨尔贡").And.NotContain("滚动");
    }

    [Fact]
    public async Task StandardModeUsesItsShorterRowsAndExcludesCovenantsOutsideItsPool()
    {
        var standard = new CovenantPool(Names.Except(["阿戈尔", "卡西米尔"]).ToArray(),
            ["迅捷", "坚守", "助力", "不屈", "调和", "协防干员", "聚合", "奥术", "精准", "远见"]);
        var scanner = new PreparationScanner(new CoreOcr
        {
            Title = "标准模拟 确认本局信息",
            Row = [
                new("核心盟约", (69, 136, 102, 30)), new("附加盟约", (72, 346, 101, 30)),
                new("炎", (47, 284, 25, 20)), new("萨尔贡", (115, 282, 87, 23)),
                new("维多利亚 谢拉格 叙拉古 乌萨斯 伊利亚 莱塔尼亚", (190, 282, 507, 23)),
                new("迅捷", (38, 491, 44, 24)), new("坚守", (122, 490, 49, 27)),
                new("助力", (207, 490, 57, 26)), new("不屈", (292, 490, 48, 26)),
                new("调和", (379, 492, 76, 23)), new("协防干员聚合", (448, 487, 153, 31)),
                new("奥术", (625, 490, 62, 27)), new("精准", (724, 491, 47, 24)),
                new("远见", (810, 491, 43, 25))]
        }, Candidates, covenantCatalog: new CovenantCatalog { ModePools = { ["标准模拟"] = standard } });

        await scanner.ProcessAsync(PreparationScannerTests.Frame("preparation-standard.png"));

        scanner.BannedNames.Should().BeEquivalentTo(
            "阿戈尔干员", "卡西米尔干员", "三核心", "无核心精准", "无核心远见", "无核心灵巧",
            "无核心奇迹", "无核心投资人", "无核心突袭", "无核心独行", "无核心绝技");
        scanner.Status.Should().Contain("禁用附加：精准、远见");
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public void ReadsDisabledCoreNamesAtBothResolutions(int width, int height)
    {
        using var stream = new MemoryStream(Frame);
        using var original = new Bitmap(stream);
        using var scaled = new Bitmap(original, width, height);
        var labels = Labels.Select(t => new OcrTextResult(t.Text,
            (t.Box.X * width / 1280, t.Box.Y * height / 720, t.Box.W * width / 1280, t.Box.H * height / 720))).ToArray();
        CoreCovenantRecognizer.FindDisabled(scaled, labels, Names).Should().BeEquivalentTo(Disabled);
    }

    [Fact]
    public void ReadsWindowedOverviewWhenCursorObscuresEndOfCoreHeading()
    {
        using var stream = new MemoryStream(PreparationScannerTests.Frame("preparation-windowed.png"));
        using var bitmap = new Bitmap(stream);
        OcrTextResult[] labels = [new("米核心盟", (16, 127, 137, 44)), new("附加盟约", (73, 347, 99, 29)),
            new("炎", (47, 283, 29, 23)), new("维多利亚", (108, 282, 76, 22)),
            new("谢拉格乌萨斯伊比利亚萨尔贡", (198, 281, 335, 25)), new("阿戈尔", (545, 284, 71, 19)),
            new("叙拉古卡西米尔莱塔尼亚", (626, 281, 241, 22))];
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, Names).Should()
            .BeEquivalentTo("萨尔贡", "阿戈尔", "叙拉古", "卡西米尔", "莱塔尼亚");
    }

    internal static IReadOnlyList<OcrTextResult> OriginalLabels => [
        new("核心盟约", (107, 206, 157, 44)), new("附加盟约", (106, 520, 153, 44)),
        new("阿戈尔", (46, 423, 85, 32)), new("叙拉古", (173, 425, 90, 31)),
        new("乌萨斯伊比利亚莱塔尼亚", (299, 422, 359, 33)), new("炎", (712, 426, 45, 29)),
        new("萨尔贡", (819, 423, 124, 35)), new("维多利亚", (932, 423, 148, 30)),
        new("谢拉格卡西米尔", (1075, 423, 228, 35)),
        .. AdditionalCovenantRecognitionTests.Texts("preparation-overview.png").Where(t => t.Box.Y > 600)];

    [Fact]
    public async Task ACompleteChangedRowReplacesAutomaticBansAndPreservesManualOverrides()
    {
        var ocr = new CoreOcr();
        var scanner = new PreparationScanner(ocr, Candidates);
        await scanner.ProcessAsync(Frame);
        scanner.SetManualBan("乌萨斯干员", true);
        scanner.SetManualBan("萨尔贡干员", false);
        ocr.Row = OriginalLabels;
        await scanner.ProcessAsync(PreparationScannerTests.Frame("preparation-overview.png"));
        scanner.BannedNames.Should().BeEquivalentTo("炎干员", "维多利亚干员", "谢拉格干员", "卡西米尔干员",
            "双核心", "三核心", "多核心无附加", "仅附加命中", "乌萨斯干员",
            "无核心精准", "无核心灵巧", "无核心助力", "无核心突袭", "无核心不屈");
    }

    [Theory]
    [InlineData("选择策略")]
    [InlineData("确定自选编队")]
    [InlineData("商店")]
    public async Task OtherPhasesPreserveBansWithoutReadingCovenants(string title)
    {
        var ocr = new CoreOcr();
        var scanner = new PreparationScanner(ocr, Candidates);
        scanner.SetManualBan("手动", true);
        ocr.Title = title;
        await scanner.ProcessAsync(Frame);
        ocr.RowCalls.Should().Be(0);
        scanner.BannedNames.Should().Equal("手动");
    }

    [Fact]
    public async Task IncompleteOcrPreservesBansAndRetriesTheSameFrame()
    {
        var ocr = new CoreOcr { Row = Labels.Take(4).ToArray() };
        var scanner = new PreparationScanner(ocr, Candidates);
        scanner.SetManualBan("手动", true);
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().Equal("手动");
        scanner.Status.Should().Contain("识别不完整");
        ocr.Row = Labels;
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().Contain("双核心").And.Contain("手动");
    }

    [Fact]
    public async Task AdditionalOnlyChangesRefreshBansEvenWhenCoreCovenantsAreUnchanged()
    {
        var ocr = new CoreOcr();
        var scanner = new PreparationScanner(ocr, Candidates);
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().Contain("双核心").And.Contain("无核心精准").And.NotContain("无核心灵巧");
        ocr.Row = Labels.Select(t => t.Text switch
        {
            "精准" => t with { Text = "灵巧" },
            "灵巧" => t with { Text = "精准" },
            _ => t
        }).ToList();
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().Contain("双核心").And.Contain("三核心").And.Contain("萨尔贡干员");
        scanner.BannedNames.Should().Contain("无核心灵巧").And.NotContain("无核心精准");
    }

    [Fact]
    public async Task MissingAdditionalLabelsPreserveTheExistingBans()
    {
        var ocr = new CoreOcr();
        var scanner = new PreparationScanner(ocr, Candidates);
        await scanner.ProcessAsync(Frame);
        var before = scanner.BannedNames;
        ocr.Row = Labels.Where(t => t.Box.Y < 400).ToList();
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().BeEquivalentTo(before);
        scanner.Status.Should().Contain("附加盟约识别不完整");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameSuccessfulRowRestoresStatusAfterAMissedObservation(bool missingLabels)
    {
        var ocr = new CoreOcr();
        var scanner = new PreparationScanner(ocr, Candidates);
        await scanner.ProcessAsync(Frame);
        var success = scanner.Status;
        if (missingLabels) ocr.Row = [];
        else ocr.Title = "商店";
        await scanner.ProcessAsync(Frame);
        scanner.Status.Should().NotBe(success);
        ocr.Title = "确认本局信息";
        ocr.Row = Labels;
        await scanner.ProcessAsync(Frame);
        scanner.Status.Should().Be(success);
    }

    [Fact]
    public void AdditionalLabelsDoNotReplaceAMissingCoreLabel()
    {
        using var stream = new MemoryStream(Frame);
        using var bitmap = new Bitmap(stream);
        var labels = Labels.Where(t => t.Text != "谢拉格阿戈尔")
            .Append(new OcrTextResult("谢拉格阿戈尔", (543, 490, 150, 23))).ToArray();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, Names).Should().BeNull();
    }

    [Fact]
    public void ObscuredIconIsUnknownInsteadOfBeingAssumedDisabled()
    {
        using var stream = new MemoryStream(Frame);
        using var bitmap = new Bitmap(stream);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.FillRectangle(Brushes.Black, 455, 180, 75, 85);
        CoreCovenantRecognizer.FindDisabled(bitmap, Labels, Names).Should().BeNull();
    }

    [Fact]
    public async Task ManualUnbanSurvivesRescanAndClearSuppressesTheUnchangedRowUntilForced()
    {
        var scanner = new PreparationScanner(new CoreOcr(), Candidates);
        int events = 0;
        scanner.BansChanged += _ => events++;
        await scanner.ProcessAsync(Frame);
        await scanner.ProcessAsync(Frame);
        events.Should().Be(1);
        scanner.SetManualBan("双核心", false);
        await scanner.ProcessAsync(Frame, force: true);
        scanner.BannedNames.Should().NotContain("双核心");
        scanner.ClearBans();
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().BeEmpty();
        await scanner.ProcessAsync(Frame, force: true);
        scanner.BannedNames.Should().Contain("双核心");
    }

    [Fact]
    public async Task FirstScanPreservesQueuedCapturesButClearInvalidatesThem()
    {
        var ocr = new CoreOcr();
        var scanner = new PreparationScanner(ocr, Candidates);
        int generation = scanner.Generation;
        await scanner.ProcessAsync(Frame, true, generation);
        await scanner.ProcessAsync(Frame, true, generation);
        ocr.RowCalls.Should().Be(4);
        scanner.ClearBans();
        await scanner.ProcessAsync(Frame, true, generation);
        ocr.RowCalls.Should().Be(4);
        scanner.BannedNames.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ClearWhileOcrIsPendingDiscardsTheStaleResult(int delayCall)
    {
        var ocr = new DelayedOcr(delayCall);
        var scanner = new PreparationScanner(ocr, Candidates);
        var task = scanner.ProcessAsync(Frame);
        await ocr.Entered.Task;
        scanner.ClearBans();
        ocr.Release.SetResult();
        await task;
        scanner.BannedNames.Should().BeEmpty();
    }

    [Fact]
    public async Task BriefMissKeepsBansButSustainedAbsenceStartsNewSession()
    {
        var clock = new ManualClock();
        var ocr = new CoreOcr();
        var scanner = new PreparationScanner(ocr, Candidates, clock);
        await scanner.ProcessAsync(Frame);
        ocr.Title = "商店";
        await scanner.ProcessAsync(Frame);
        clock.Now += TimeSpan.FromSeconds(5);
        ocr.Title = "确认本局信息";
        ocr.Row = [];
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().Contain("双核心");
        ocr.Title = "商店";
        await scanner.ProcessAsync(Frame);
        clock.Now += TimeSpan.FromSeconds(31);
        ocr.Title = "确认本局信息";
        await scanner.ProcessAsync(Frame);
        scanner.BannedNames.Should().BeEmpty();
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DelayedOcr(int delayCall) : IOcrEngine
    {
        private int _calls;
        public TaskCompletionSource Entered { get; } = new();
        public TaskCompletionSource Release { get; } = new();
        public async Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height)
        {
            if (++_calls == delayCall) { Entered.TrySetResult(); await Release.Task; }
            return y == 0 ? [new("确认本局信息", (400, 20, 200, 30))] : Labels;
        }
    }

    internal sealed class CoreOcr : IOcrEngine
    {
        public string Title { get; set; } = "确认本局信息";
        public IReadOnlyList<OcrTextResult> Row { get; set; } = Labels;
        public int RowCalls { get; private set; }
        public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height)
        {
            if (y == 0) return Task.FromResult<IReadOnlyList<OcrTextResult>>([new(Title, (400, 20, 200, 30))]);
            RowCalls++;
            return Task.FromResult(Row);
        }
    }
}
