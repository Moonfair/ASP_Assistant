using System.Drawing;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class ReportedCovenantRecognitionTests
{
    private const string FileName = "preparation-reported-incomplete.png";

    [Fact]
    public async Task RecognizesBansFromTheReportedInsetPreparationGrid()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = await new JsonDataStore(data).LoadCovenantCatalogAsync();
        using var ocr = new MaaOcrEngine(data);
        var scanner = new PreparationScanner(ocr, [], covenantCatalog: catalog);

        await scanner.ProcessAsync(PreparationScannerTests.Frame("preparation-reported-547p.png"));

        scanner.Phase.Should().Be(PreparationPhase.Information);
        scanner.CovenantAvailability.Should().NotBeNull(scanner.Status);
        var pool = catalog.ModePools["终极模拟"];
        pool.CoreCovenants.Except(scanner.CovenantAvailability!.EnabledCore).Should()
            .BeEquivalentTo("维多利亚", "叙拉古", "乌萨斯", "伊比利亚", "莱塔尼亚");
        pool.AdditionalCovenants.Except(scanner.CovenantAvailability.EnabledAdditional).Should()
            .BeEquivalentTo("灵巧", "坚守", "助力", "突袭", "聚合");
    }

    // Native OCR from the user's failing windowed screenshot: a green 阿戈尔 is read as 阿尔.
    internal static IReadOnlyList<OcrTextResult> Texts => [
        new("核心盟约", (64, 136, 106, 30)), new("附加盟约", (64, 344, 104, 33)),
        new("萨尔贡", (24, 282, 85, 24)),
        new("维多利亚 阿尔 伊比利亚 莱塔尼亚", (97, 283, 339, 22)),
        new("炎", (469, 284, 29, 20)),
        new("谢拉格叙拉古卡西米尔乌萨斯", (537, 282, 318, 23))];

    [Fact]
    public void MissingCharacterOnGreenIconDoesNotInvalidateReadableDisabledNames()
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        CoreCovenantRecognizer.FindDisabled(bitmap, Texts, CoreCovenantBanTests.Names).Should()
            .BeEquivalentTo("炎", "谢拉格", "叙拉古", "卡西米尔", "乌萨斯");
    }

    [Fact]
    public void MissingDisabledNameStillDefersTheObservation()
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        CoreCovenantRecognizer.FindDisabled(bitmap, Texts.Where(t => t.Text != "炎").ToList(), CoreCovenantBanTests.Names)
            .Should().BeNull();
    }

    [Fact]
    public void UnreadableGreenNameStillRequiresAConfidentIconState()
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.FillRectangle(Brushes.Black, 190, 190, 85, 90);
        CoreCovenantRecognizer.FindDisabled(bitmap, Texts, CoreCovenantBanTests.Names).Should().BeNull();
    }

    [Fact]
    public void DuplicateDisabledNamesAreRejected()
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        var labels = Texts.Select(t => t with { Text = t.Text.Replace("乌萨斯", "谢拉格") }).ToList();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, CoreCovenantBanTests.Names).Should().BeNull();
    }

    [Theory]
    [InlineData("谢拉")]
    [InlineData("谢拉各")]
    [InlineData("谢拉拉格")]
    public void OneCharacterErrorOnADisabledLongNameIsRecoveredInsideAMergedBox(string recognized)
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        var labels = Texts.Select(t => t with { Text = t.Text.Replace("谢拉格", recognized) }).ToList();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, CoreCovenantBanTests.Names).Should()
            .BeEquivalentTo("炎", "谢拉格", "叙拉古", "卡西米尔", "乌萨斯");
    }

    [Fact]
    public void MissingCharacterOnDisabledAegirIsRecovered()
    {
        using var input = new MemoryStream(CoreCovenantBanTests.Frame);
        using var bitmap = new Bitmap(input);
        var labels = CoreCovenantBanTests.Labels.Select(t => t with { Text = t.Text.Replace("阿戈尔", "阿尔") }).ToList();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, CoreCovenantBanTests.Names).Should()
            .BeEquivalentTo(CoreCovenantBanTests.Disabled);
    }

    [Fact]
    public void AmbiguousFuzzyNameIsNotResolvedByVocabularyOrder()
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        var labels = Texts.Select(t => t with { Text = t.Text.Replace("谢拉格", "谢拉").Replace("萨尔贡", "谢拉各") }).ToList();
        var names = CoreCovenantBanTests.Names.Select(n => n == "萨尔贡" ? "谢拉各" : n).ToArray();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, names).Should().BeNull();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, names.Reverse()).Should().BeNull();
    }

    [Theory]
    [InlineData("谢各")]
    [InlineData("谢")]
    public void TooLittleEvidenceDoesNotIdentifyADisabledName(string recognized)
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        var labels = Texts.Select(t => t with { Text = t.Text.Replace("谢拉格", recognized) }).ToList();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, CoreCovenantBanTests.Names).Should().BeNull();
    }

    [Fact]
    public void ExactNameTakesPrecedenceOverSimilarVocabularyEntries()
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        var names = CoreCovenantBanTests.Names.Select(n => n == "萨尔贡" ? "谢拉各" : n).ToArray();
        var labels = Texts.Select(t => t with { Text = t.Text.Replace("萨尔贡", "谢拉各") }).ToList();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, names).Should()
            .BeEquivalentTo("炎", "谢拉格", "叙拉古", "卡西米尔", "乌萨斯");
    }

    [Fact]
    public void FuzzyNamesStillCannotDuplicateAnotherSlot()
    {
        using var input = new MemoryStream(PreparationScannerTests.Frame(FileName));
        using var bitmap = new Bitmap(input);
        var labels = Texts.Select(t => t with { Text = t.Text.Replace("乌萨斯", "谢拉") }).ToList();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, CoreCovenantBanTests.Names).Should().BeNull();
    }

    [Fact]
    public void MissingWholeNamesInsideAMergedBoxCannotShiftAGreenNameOntoAGrayIcon()
    {
        using var input = new MemoryStream(CoreCovenantBanTests.Frame);
        using var bitmap = new Bitmap(input);
        var labels = CoreCovenantBanTests.Labels.Select(t => t.Box.X == 106
            ? t with { Text = "叙拉古 卡西米尔 莱塔尼亚" } : t).ToList();
        CoreCovenantRecognizer.FindDisabled(bitmap, labels, CoreCovenantBanTests.Names).Should().BeNull();
    }
}
