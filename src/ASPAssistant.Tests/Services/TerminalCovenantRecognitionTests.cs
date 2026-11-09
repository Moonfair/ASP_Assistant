using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class TerminalCovenantRecognitionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RereadsIsolatedGrayLabelsAndRequiresAllOfThemBeforePublishing(bool complete)
    {
        var candidates = CoreCovenantBanTests.Names.Select(n => new BanCandidate(n, [n], [], [])).ToList();
        var ocr = new TerminalOcr(complete);
        var scanner = new PreparationScanner(ocr, candidates);
        scanner.SetManualBan("炎", true);

        await scanner.ProcessAsync(PreparationScannerTests.Frame("preparation-terminal.png"));

        ocr.Rereads.Should().Be(1);
        scanner.BannedNames.Should().BeEquivalentTo(complete
            ? new[] { "炎", "维多利亚", "叙拉古", "卡西米尔", "伊比利亚", "莱塔尼亚" }
            : new[] { "炎" });
    }

    private sealed class TerminalOcr(bool complete) : IOcrEngine
    {
        public int Rereads { get; private set; }

        public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height)
        {
            if (y > 0)
            {
                // Native OCR from the reported frame. The combined box loses multiple characters.
                return Task.FromResult<IReadOnlyList<OcrTextResult>>([
                    new("核心盟约", (63, 136, 107, 30)), new("附加盟约", (64, 344, 104, 33)),
                    new("炎", (42, 283, 24, 22)), new("萨尔贡", (108, 282, 86, 24)),
                    new("谢拉格 阿尔 乌萨斯 维多利亚 叙拉古 卡尔 伊利亚 莱塔尼亚", (194, 281, 671, 26))]);
            }
            if (width > 500)
                return Task.FromResult<IReadOnlyList<OcrTextResult>>([new("确认本局信息", (400, 20, 200, 30))]);

            Rereads++;
            string[] names = ["维多利亚", "叙拉古", complete ? "卡西米尔" : "卡尔", "伊比利亚", "莱塔尼亚"];
            return Task.FromResult<IReadOnlyList<OcrTextResult>>(names
                .Select((name, index) => new OcrTextResult(name, (24, index * (height / 5) + 28, 140, 42)))
                .Reverse().ToArray());
        }
    }
}
