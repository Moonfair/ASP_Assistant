using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public sealed class GarrisonBattleVoiceTests : IDisposable
{
    private readonly MaaOcrEngine _ocr = new(Path.Combine(AppContext.BaseDirectory, "data"));

    [Theory]
    [InlineData("bob-victory.png", 1280, 5, 16, BobVoiceCue.BattleVictory)]
    [InlineData("bob-victory.png", 960, 5, 16, BobVoiceCue.BattleVictory)]
    [InlineData("bob-loss.png", 1280, 4, 27, BobVoiceCue.BattleLoss)]
    [InlineData("bob-loss.png", 960, 4, 27, BobVoiceCue.BattleLoss)]
    [InlineData("bob-precombat.png", 1280, 6, 16, BobVoiceCue.BeforeBattle)]
    [InlineData("bob-precombat.png", 960, 6, 16, BobVoiceCue.BeforeBattle)]
    public async Task ReadsCombatScreensAndNarrowsHealthToTheCurrentValue(
        string file, int width, int round, int health, BobVoiceCue expected)
    {
        using var original = Load(file);
        using var resized = new Bitmap(original, width, width * original.Height / original.Width);
        var result = await Recognize(resized);
        Assert.Equal(round, result.Hud.Round);
        Assert.Equal(health, result.Voice.Health);
        Assert.Equal(expected, result.Voice.BattleCue);
    }

    [Fact]
    public async Task ThreeReadyPlayersTriggerEvenWhenTheTopRightIsNotRed()
    {
        using var source = Load("bob-precombat.png");
        for (int y = 12; y < 77; y++)
        for (int x = 1090; x < 1280; x++)
        {
            var pixel = source.GetPixel(x, y);
            source.SetPixel(x, y, Color.FromArgb(pixel.G, pixel.G, pixel.B));
        }
        Assert.Null((await Recognize(source)).Voice.BattleCue);
        using (var graphics = Graphics.FromImage(source))
        {
            using var ready = source.Clone(new Rectangle(1151, 143, 17, 37), PixelFormat.Format32bppArgb);
            graphics.DrawImageUnscaled(ready, 1190, 143);
        }
        Assert.Equal(BobVoiceCue.BeforeBattle, (await Recognize(source)).Voice.BattleCue);
    }

    [Fact]
    public async Task RecognizesRestAndHealthWithTheEarlierHudTheme()
    {
        using var source = Load("shop-new-skin.png");
        var result = await Recognize(source);
        Assert.True(result.Voice.Preparation);
        Assert.Equal(29, result.Voice.Health);
        Assert.Null(result.Voice.BattleCue);
    }

    [Theory]
    [InlineData("shop-new-skin.png")]
    [InlineData("bob-precombat.png")]
    public async Task ShopOrUnrelatedRedLightingDoesNotTrigger(string file)
    {
        using var source = Load(file);
        if (file == "bob-precombat.png")
        {
            using var graphics = Graphics.FromImage(source);
            graphics.FillRectangle(Brushes.Black, 1110, 92, 160, 96);
        }
        Assert.Null((await Recognize(source)).Voice.BattleCue);
    }

    private Task<(GarrisonHud Hud, GarrisonVoiceFrame Voice)> Recognize(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return new GarrisonHudRecognizer(_ocr).RecognizeWithVoiceAsync(stream.ToArray(), includeVoice: true);
    }

    private static Bitmap Load(string file) => new(Path.Combine(AppContext.BaseDirectory, "TestData", file));

    public void Dispose() => _ocr.Dispose();
}
