using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class PreparationScannerTests
{
    internal static byte[] Frame(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    [Theory]
    [InlineData("1/3 确认本局信息", PreparationPhase.Information)]
    [InlineData("2/3 选择策略", PreparationPhase.Strategy)]
    [InlineData("3/3 确定自选编队", PreparationPhase.Squad)]
    [InlineData("商店 休整期", PreparationPhase.None)]
    public void ClassifiesPreparationStages(string text, PreparationPhase expected) =>
        PreparationScanner.Classify([text]).Should().Be(expected);

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public void FindsAllNineteenVisibleBanCardsAtBothResolutions(int width, int height)
    {
        using var input = new MemoryStream(Frame("preparation-bans.png"));
        using var original = new Bitmap(input);
        using var scaled = new Bitmap(original, width, height);
        using var output = new MemoryStream();
        scaled.Save(output, ImageFormat.Png);
        var cards = BanCardLocator.FindCards(output.ToArray());
        cards.Should().HaveCount(19);
        cards.Should().Contain(c => Math.Abs(c.Left - width * 263.0 / 1920) < 5);
        cards.Should().OnlyContain(c => c.Right < width * .70 && c.Bottom < height * .96);
    }

    [Theory]
    [InlineData("preparation-overview.png")]
    [InlineData("preparation-squad.png")]
    public void OtherPreparationContentIsNotABanCardList(string file) =>
        BanCardLocator.FindCards(Frame(file)).Should().BeEmpty();

    [Theory]
    [InlineData(-400, 9)]
    [InlineData(122, 17)]
    public void ScrollingDefersCardsClippedByTheListViewport(int offset, int expected)
    {
        using var input = new MemoryStream(Frame("preparation-bans.png"));
        using var original = new Bitmap(input);
        using var scrolled = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(scrolled)) g.DrawImage(original, 0, offset);
        using var output = new MemoryStream();
        scrolled.Save(output, ImageFormat.Png);
        BanCardLocator.FindCards(output.ToArray()).Should().HaveCount(expected);
    }

    [Theory]
    [InlineData(265, 309, 121, 121, true)]
    [InlineData(263, 307, 130, 130, false)]
    [InlineData(249, 270, 888, 565, false)]
    [InlineData(417, 309, 121, 121, false)]
    public void RejectsFallbackAndWrongSlotFeatureBoxes(int x, int y, int w, int h, bool valid) =>
        MaaBanIconMatcher.IsPlausiblePortrait(new(x, y, w, h), new(263, 307, 130, 130)).Should().Be(valid);

}
