using System.Text.Json;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace ASPAssistant.Tests.Services;

public class ShopRecognizerTests
{
    private static IReadOnlyList<OcrTextResult> ReportedTexts => JsonSerializer.Deserialize<List<OcrTextResult>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-new-skin-ocr.json")),
        new JsonSerializerOptions { IncludeFields = true })!;

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task NewSkinFindsEquipmentAndBothCopiesOfTheSameOperator(int width, int height)
    {
        double scale = height / 720.0;
        var texts = ReportedTexts.Select(t => t with { Box = (
            (int)(t.Box.X * scale), (int)(t.Box.Y * scale),
            (int)(t.Box.W * scale), (int)(t.Box.H * scale)) }).ToList();
        var items = await Recognize(texts, ["源石溶剂", "卡达", "角峰", "隐现"], width, height);

        items.Select(i => i.Name).Should().Equal("源石溶剂", "卡达", "卡达", "角峰");
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        var centers = items.Select(i => i.OcrRegion.X + i.OcrRegion.Width / 2).ToArray();
        centers[0].Should().BeApproximately(.414, .02);
        centers[1].Should().BeApproximately(.540, .02);
        centers[2].Should().BeApproximately(.667, .02);
        centers[3].Should().BeApproximately(.793, .02);
        items.Should().OnlyContain(i => i.OcrRegion.Y > .69 && i.OcrRegion.Y < .75
            && i.OcrRegion.X + i.OcrRegion.Width <= 1 && i.OcrRegion.Y + i.OcrRegion.Height <= 1);
    }

    [Fact]
    public async Task NamesMustMatchWholeLabelsAndStayInsideTheShopNameRow()
    {
        var items = await Recognize([
            new("角峰", (400, 300, 40, 20)), // field or another UI panel
            new("耀骑士临光", (485, 664, 95, 21)),
            new("「卡 达」", (646, 663, 50, 22)),
            new("源石溶剂", (973, 664, 80, 20))], ["临光", "角峰", "卡达"]);
        items.Select(i => i.Name).Should().Equal("卡达");
    }

    [Fact]
    public async Task TracksProtocolVariantByItsInGameName()
    {
        var items = await Recognize([new("Touch", (620, 664, 50, 21))], ["Touch(卫戍协议)"]);
        items.Select(item => item.Name).Should().Equal("Touch(卫戍协议)");
    }

    [Fact]
    public async Task DuplicateOcrBoxesDoNotCreateDuplicateMarkers()
    {
        var label = new OcrTextResult("角峰", (973, 664, 37, 20));
        var items = await Recognize([label, label], ["角峰"]);
        items.Should().ContainSingle();
    }

    [Fact]
    public async Task RereadsNameWithoutTheProfessionIconWhenWholeLabelOcrIsWrong()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 648, 1280, 62))
            .ReturnsAsync([new("入隐现", (621, 680, 56, 22)), new("深", (948, 679, 60, 23))]);
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 643, 675, 34, 32))
            .ReturnsAsync([new("隐现", (643, 675, 34, 32))]);
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 971, 674, 37, 33))
            .ReturnsAsync([new("深靛", (971, 674, 37, 33))]);
        var items = await new ShopRecognizer(ocr.Object).RecognizeAsync([], 1280, 720,
            ["隐现", "深靛"], Region);
        items.Select(i => i.Name).Should().Equal("隐现", "深靛");
    }

    [Fact]
    public async Task ReadableShopDoesNotSpendOcrCallsOnDecorationOrButtons()
    {
        var ocr = new Mock<IOcrEngine>();
        int calls = 0;
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((byte[] _, int x, int y, int w, int h) =>
            {
                calls++;
                return (IReadOnlyList<OcrTextResult>)(x == 0 && w == 1280 ? ReportedTexts : []);
            });
        var items = await new ShopRecognizer(ocr.Object, ["源石溶剂", "卡达", "角峰"])
            .RecognizeAsync([], 1280, 720, ["卡达"], Region);
        items.Select(i => i.Name).Should().Equal("卡达", "卡达");
        calls.Should().Be(1, "readable cards must not wait behind OCR retries for decorations or untracked cards");
    }

    [Fact]
    public async Task KnownNameRowSkipsTraitAndFooterButStillRecoversAnUnreadableCard()
    {
        var ocr = new Mock<IOcrEngine>();
        int calls = 0;
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((byte[] _, int x, int y, int w, int h) =>
            {
                calls++;
                return (IReadOnlyList<OcrTextResult>)(x == 0 ? [
                    new("角峰", (973, 680, 37, 20)),
                    new("拉特兰", (621, 637, 66, 22)), new("迅捷", (621, 656, 55, 22)),
                    new("入隐现", (621, 680, 56, 22)), new("DECORATION", (622, 705, 140, 10)),
                    new("收起", (1168, 680, 40, 20))]
                    : x == 643 && y == 675 ? [new("隐现", (643, 675, 34, 32))] : []);
            });
        var items = await new ShopRecognizer(ocr.Object, ["角峰", "隐现"])
            .RecognizeAsync([], 1280, 720, ["隐现"], Region);
        items.Select(i => i.Name).Should().Equal("隐现");
        calls.Should().Be(2, "only the unreadable card name needs a retry");
    }

    [Fact]
    public async Task InitialScanUsesOnlyTheNameStrip()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync([]);
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 648, 1280, 62))
            .ReturnsAsync(ReportedTexts);
        var items = await new ShopRecognizer(ocr.Object).RecognizeAsync([], 1280, 720,
            ["源石溶剂", "卡达", "角峰"], Region);
        items.Select(i => i.Name).Should().Equal("源石溶剂", "卡达", "卡达", "角峰");
    }

    [Fact]
    public async Task TinyIconFragmentDoesNotTriggerAnotherNativeRecognition()
    {
        var ocr = new Mock<IOcrEngine>();
        int calls = 0;
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((byte[] _, int x, int y, int w, int h) =>
            {
                calls++;
                return (IReadOnlyList<OcrTextResult>)(x == 0 ? [
                    new("C", (618, 665, 18, 15)), new("卡达", (653, 663, 40, 22))] : []);
            });
        var items = await new ShopRecognizer(ocr.Object).RecognizeAsync([], 1280, 720, ["卡达"], Region);
        items.Should().ContainSingle(i => i.Name == "卡达");
        calls.Should().Be(1, "the three-pixel remainder of an icon cannot contain a name");
    }

    [Fact]
    public async Task PaddedTextBoxDoesNotCropAwayTheFirstCharacterOfTheName()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync([]);
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 648, 1280, 62))
            .ReturnsAsync([new("入隐现", (622, 675, 55, 31))]);
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 640, 670, 37, 41))
            .ReturnsAsync([new("隐现", (643, 680, 34, 22))]);
        var items = await new ShopRecognizer(ocr.Object).RecognizeAsync([], 1280, 720, ["隐现"], Region);
        items.Should().ContainSingle(i => i.Name == "隐现");
    }

    private static ASPAssistant.Core.GameModes.OcrRegionDefinition Region =>
        new ASPAssistant.Core.GameModes.GarrisonProtocol.GarrisonOcrStrategy()
            .GetScanRegions().Single(r => r.Name == "Shop");

    private static Task<IReadOnlyList<ASPAssistant.Core.Models.ShopItem>> Recognize(
        IReadOnlyList<OcrTextResult> texts, IReadOnlyList<string> names, int width = 1280, int height = 720)
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync([]);
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, (int)(height * .90),
            width, (int)Math.Ceiling(height * .985) - (int)(height * .90))).ReturnsAsync(texts);
        return new ShopRecognizer(ocr.Object).RecognizeAsync([], width, height, names, Region);
    }
}
