using ASPAssistant.Core.Imaging;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.GameModes.GarrisonProtocol;

namespace ASPAssistant.Mac.Tests;

public class MacRecognitionTests
{
    [Fact]
    public void FindsNineteenBanCardsInRealScreenshot()
    {
        var png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "preparation-bans.png"));
        Assert.Equal(19, BanCardLocator.FindCards(png).Count);
    }

    [Fact]
    public void ReadsDisabledCoreCovenantsFromRealScreenshot()
    {
        var png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "preparation-core-bans.png"));
        using var stream = new MemoryStream(png);
        using var image = new PortableBitmap(stream);
        string[] names = ["炎", "维多利亚", "叙拉古", "卡西米尔", "莱塔尼亚", "萨尔贡", "谢拉格", "阿戈尔", "乌萨斯", "伊比利亚"];
        OcrTextResult[] labels = [
            new("核心盟约部分盟约所含干员阵容不完整", (16, 123, 464, 41)),
            new("附加盟约", (72, 343, 101, 27)),
            new("炎", (46, 278, 43, 22)),
            new("维多利亚 叙拉古 卡西米尔 莱塔尼亚 萨尔贡", (106, 276, 445, 26)),
            new("谢拉格阿戈尔", (543, 277, 150, 23)),
            new("乌萨斯伊比利亚", (684, 276, 184, 23))
        ];
        var disabled = CoreCovenantRecognizer.FindDisabled(image, labels, names);
        Assert.NotNull(disabled);
        Assert.Equal(names[5..].Order(), disabled.Order());
    }

    [Fact]
    public async Task ReadsShopHudUsingMacMaaRuntime()
    {
        using var ocr = new MaaOcrEngine(Path.Combine(AppContext.BaseDirectory, "data"));
        var png = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-new-skin.png"));
        var hud = await new GarrisonHudRecognizer(ocr).RecognizeAsync(png);
        Assert.Equal(new GarrisonHud(1, 1, 4, 5), hud);
    }

    [Fact]
    public async Task FindsTrackedShopItemsUsingMacMaaRuntime()
    {
        using var ocr = new MaaOcrEngine(Path.Combine(AppContext.BaseDirectory, "data"));
        var png = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-new-skin.png"));
        var region = new GarrisonOcrStrategy().GetScanRegions().Single(item => item.Name == "Shop");
        var names = new[] { "源石溶剂", "卡达", "角峰", "隐现" };
        var items = await new ShopRecognizer(ocr, names).RecognizeAsync(png, 1280, 720, names, region);
        Assert.Contains(items, item => item.Name == "卡达");
        Assert.Contains(items, item => item.Name == "角峰");
    }
}
