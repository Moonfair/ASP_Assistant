using System.Drawing;
using System.Text.Json;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class AdditionalCovenantRecognitionTests
{
    internal static readonly string[] Names = ["精准", "迅捷", "灵巧", "坚守", "助力", "远见", "奇迹", "投资人", "突袭", "不屈", "调和", "协防干员", "独行", "绝技", "聚合", "奥术"];
    internal static IReadOnlyList<OcrTextResult> Texts(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "covenant-ocr.json")));
        var data = doc.RootElement.EnumerateArray().Single(x => x.GetProperty("file").GetString() == file);
        return data.GetProperty("texts").EnumerateArray().Select(t => new OcrTextResult(t.GetProperty("text").GetString()!,
            (t.GetProperty("x").GetInt32(), t.GetProperty("y").GetInt32(), t.GetProperty("w").GetInt32(), t.GetProperty("h").GetInt32()))).ToList();
    }

    [Theory]
    [InlineData("preparation-core-bans.png", "精准,迅捷,助力,远见,奥术")]
    [InlineData("preparation-overview.png", "精准,灵巧,助力,突袭,不屈")]
    [InlineData("preparation-windowed.png", "助力,远见,奇迹,突袭,聚合")]
    public void ReadsBothAdditionalRowsInArbitraryOrder(string file, string expected)
    {
        using var stream = new MemoryStream(PreparationScannerTests.Frame(file));
        using var bitmap = new Bitmap(stream);
        AdditionalCovenantRecognizer.FindDisabled(bitmap, Texts(file), Names).Should().BeEquivalentTo(expected.Split(','));
    }

    [Fact]
    public void UnreadableDisabledLabelDefersTheObservation()
    {
        const string file = "preparation-core-bans.png";
        using var stream = new MemoryStream(PreparationScannerTests.Frame(file));
        using var bitmap = new Bitmap(stream);
        AdditionalCovenantRecognizer.FindDisabled(bitmap, Texts(file).Where(t => t.Text != "精准").ToList(), Names).Should().BeNull();
    }

    [Fact]
    public void UnknownIconCannotBeAssumedEnabled()
    {
        const string file = "preparation-core-bans.png";
        using var stream = new MemoryStream(PreparationScannerTests.Frame(file));
        using var bitmap = new Bitmap(stream);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.FillRectangle(Brushes.Black, 460, 550, 80, 80);
        AdditionalCovenantRecognizer.FindDisabled(bitmap, Texts(file), Names).Should().BeNull();
    }

    [Theory]
    [InlineData("投资人", "投责人")]
    [InlineData("投资人", "投人")]
    [InlineData("协防干员", "协防千员")]
    public void DisabledLongAdditionalNameUsesTheSameConservativeFuzzyMatching(string name, string recognized)
    {
        const string file = "preparation-overview.png";
        using var stream = new MemoryStream(PreparationScannerTests.Frame(file));
        using var bitmap = new Bitmap(stream);
        // Put the long name into a disabled slot; move 精准 to its enabled slot.
        var texts = Texts(file).Select(t => t with { Text = t.Text == "精准" ? recognized : t.Text == name ? "精准" : t.Text }).ToList();
        AdditionalCovenantRecognizer.FindDisabled(bitmap, texts, Names).Should()
            .BeEquivalentTo(name, "灵巧", "助力", "突袭", "不屈");
    }

    [Theory]
    [InlineData("精")]
    [InlineData("精淮")]
    public void ShortAdditionalNamesRequireAnExactMatch(string recognized)
    {
        const string file = "preparation-overview.png";
        using var stream = new MemoryStream(PreparationScannerTests.Frame(file));
        using var bitmap = new Bitmap(stream);
        var texts = Texts(file).Select(t => t with { Text = t.Text == "精准" ? recognized : t.Text }).ToList();
        AdditionalCovenantRecognizer.FindDisabled(bitmap, texts, Names).Should().BeNull();
    }

    [Fact]
    public void MissingWholeAdditionalNameCannotStretchItsGreenNeighborIntoAGraySlot()
    {
        const string file = "preparation-overview.png";
        using var stream = new MemoryStream(PreparationScannerTests.Frame(file));
        using var bitmap = new Bitmap(stream);
        var texts = Texts(file).Where(t => t.Text is not ("奥术" or "精准")).ToList();
        // A merged box spans both 奥术 and 精准, but OCR only returns the green name.
        texts.Add(new("奥术", (53, 949, 250, 47)));
        AdditionalCovenantRecognizer.FindDisabled(bitmap, texts, Names).Should().BeNull();
    }
}
