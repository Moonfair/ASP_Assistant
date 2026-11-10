using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class GroupedCovenantRecognitionTests
{
    [Fact]
    public async Task ScrollCompletesAdditionalCovenantsAndKeepsTheLastSelectedCoreGroup()
    {
        var ocr = new GroupedOcr();
        var scanner = CreateScanner(ocr);
        var notices = new List<PreparationNotice>();
        scanner.StatusChanged += notices.Add;
        await scanner.ProcessAsync(Frame("top"));
        scanner.Status.Should().Contain("向下").And.Contain("莱塔尼亚");
        scanner.BannedNames.Should().BeEmpty();
        scanner.CovenantAvailability.Should().BeNull();
        notices[^1].Kind.Should().Be(PreparationNoticeKind.ScrollRequired);

        // The core groups can be entirely off screen when the player reveals the lower row.
        ocr.Scrolled = true;
        ocr.HideCoreLabels = true;
        await scanner.ProcessAsync(ChangeFrame("scrolled", (graphics, _) =>
            graphics.FillRectangle(Brushes.Black, 0, 108, 896, 255)));
        scanner.Status.Should().Contain("已锁定");
        notices[^1].Kind.Should().Be(PreparationNoticeKind.Result);
        notices[^1].IsNewResult.Should().BeTrue();
        scanner.BannedNames.Should().Contain("乌萨斯干员").And.NotContain("莱塔尼亚干员");
        scanner.BannedNames.Should().Contain("无核心迅捷").And.NotContain("无核心精准");
        scanner.CovenantAvailability!.EnabledCore.Should().Contain("莱塔尼亚").And.NotContain("乌萨斯");
        scanner.CovenantAvailability.EnabledAdditional.Should().Contain("精准").And.NotContain("迅捷");

        await scanner.ProcessAsync(Frame("scrolled"));
        notices.Count(notice => notice.IsNewResult).Should().Be(1);
        scanner.NotifyCaptureUnavailable();
        notices[^1].Kind.Should().Be(PreparationNoticeKind.Dismiss);
    }

    [Fact]
    public async Task SwitchingGroupsKeepsLockedAdditionalCovenantsUntilTheNextRound()
    {
        var ocr = new GroupedOcr { Scrolled = true };
        var scanner = CreateScanner(ocr);
        await scanner.ProcessAsync(Frame("scrolled"));
        scanner.BannedNames.Should().Contain("乌萨斯干员").And.Contain("无核心迅捷");

        ocr.SwapAdditionalLabels = true;
        await scanner.ProcessAsync(ChangeFrame("scrolled", (graphics, image) =>
        {
            // Move the highlighted group's selection bar to the right.
            using var bar = image.Clone(new Rectangle(22, 342, 423, 8), PixelFormat.Format32bppArgb);
            graphics.DrawImageUnscaled(bar, 453, 342);
            graphics.FillRectangle(Brushes.Black, 22, 342, 423, 8);
        }));
        scanner.BannedNames.Should().NotContain("乌萨斯干员").And.Contain("莱塔尼亚干员");
        scanner.CovenantAvailability!.EnabledCore.Should().Contain("乌萨斯").And.NotContain("莱塔尼亚");
        scanner.BannedNames.Should().Contain("无核心迅捷").And.NotContain("无核心精准");

        ocr.Heading = "确定自选编队";
        var selectedCovenants = scanner.CovenantAvailability;
        await scanner.ProcessAsync(Frame("scrolled"));
        scanner.CovenantAvailability.Should().BeSameAs(selectedCovenants);
        scanner.BannedNames.Should().Contain("莱塔尼亚干员");
        ocr.Heading = "确认本局信息";
        await scanner.ProcessAsync(Frame("scrolled"));
        scanner.BannedNames.Should().Contain("乌萨斯干员").And.NotContain("莱塔尼亚干员");
        scanner.BannedNames.Should().Contain("无核心精准").And.NotContain("无核心迅捷");
    }

    private static PreparationScanner CreateScanner(IOcrEngine ocr)
    {
        var pool = new CovenantPool(CoreCovenantBanTests.Names, AdditionalCovenantRecognitionTests.Names)
        {
            SelectCoreGroup = true
        };
        var catalog = new CovenantCatalog { ModePools = { ["规程等级7"] = pool } };
        return new PreparationScanner(ocr, CoreCovenantBanTests.Candidates, covenantCatalog: catalog);
    }

    private static byte[] Frame(string state) => PreparationScannerTests.Frame($"preparation-grouped-{state}.png");

    private static byte[] ChangeFrame(string state, Action<Graphics, Bitmap> change)
    {
        using var input = new MemoryStream(Frame(state));
        using var bitmap = new Bitmap(input);
        using (var graphics = Graphics.FromImage(bitmap)) change(graphics, bitmap);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private sealed class GroupedOcr : IOcrEngine
    {
        public string Heading { get; set; } = "确认本局信息";
        public bool Scrolled { get; set; }
        public bool HideCoreLabels { get; set; }
        public bool SwapAdditionalLabels { get; set; }

        public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height)
        {
            if (y == 0)
                return Task.FromResult<IReadOnlyList<OcrTextResult>>([
                    new("规程等级7", (97, 24, 133, 36)), new(Heading, (437, 21, 198, 30))]);

            OcrTextResult[] labels = [
                new("附加盟约", (73, 497, 99, 30)),
                new("精准", (37, 640, 44, 28)), new("坚守", (124, 641, 42, 27)),
                new("助力", (210, 642, 42, 24)), new("远见", (296, 642, 41, 24)),
                new("投资人", (373, 641, 60, 26)), new("不屈", (468, 642, 43, 24)),
                new("调和", (553, 641, 46, 26)), new("协防干员", (623, 643, 76, 22)),
                new("独行", (725, 642, 42, 24)), new("绝技", (811, 641, 43, 27)),
                new("奥术", (37, 785, 44, 27)), new("迅捷", (122, 785, 45, 27)),
                new("灵巧", (210, 786, 42, 24)), new("奇迹", (295, 785, 44, 27)),
                new("突袭", (382, 787, 42, 24)), new("聚合", (466, 785, 44, 28))];
            if (!HideCoreLabels)
                labels = [.. labels,
                    new("核心盟约", (70, 136, 100, 29)),
                    new("莱塔尼亚", (98, 303, 73, 21)), new("卡西米尔", (197, 303, 73, 22)),
                    new("炎", (321, 303, 23, 22)), new("伊比利亚", (147, 433, 73, 22)),
                    new("叙拉古", (253, 433, 59, 25)), new("乌萨斯", (536, 301, 57, 25)),
                    new("维多利亚", (628, 302, 73, 22)), new("萨尔贡", (732, 300, 61, 28)),
                    new("谢拉格", (585, 432, 57, 26)), new("阿戈尔", (684, 433, 58, 25))];

            return Task.FromResult<IReadOnlyList<OcrTextResult>>(labels.Select(t =>
            {
                var box = t.Box;
                var name = SwapAdditionalLabels ? t.Text switch { "精准" => "迅捷", "迅捷" => "精准", _ => t.Text } : t.Text;
                return new OcrTextResult(name, (box.X, box.Y - (Scrolled ? 124 : 0), box.W, box.H));
            }).Where(t => t.Box.Y >= y && t.Box.Y + t.Box.H <= y + height).ToArray());
        }
    }
}
