using System.Drawing;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed class PreparationEnemyRecognizer(IOcrEngine ocr, string mapDirectory)
{
    private static readonly string[] EnemyTypes = ["特异", "飞行", "频次", "元素", "持续", "隐匿", "折射", "致命", "复苏"];
    private readonly PreparationMapMatcher _mapMatcher = new(mapDirectory);

    internal async Task<PreparationEnemyInfo?> ReadAsync(byte[] png, Bitmap image, string? mode, int? knownMap)
    {
        int x = (int)(image.Width * .72);
        double height = 720 * GarrisonFrameGeometry.UiScale(image.Width, image.Height);
        int y = (int)(height * .38);
        int bottom = (int)(image.Height - height * .16);
        var labels = await ocr.RecognizeRegionAsync(png, x, y, image.Width - x, bottom - y);
        var header = labels.FirstOrDefault(text => Compact(text.Text).Contains("敌方领袖", StringComparison.Ordinal));
        if (header == null) return null;

        // The labels can wrap or be split by the mouse pointer.
        double textLeft = header.Box.X - header.Box.H * 1.8;
        var nameLabels = labels.Where(text => text.Box.X > textLeft &&
            CenterY(text) > CenterY(header) + height * .026 && CenterY(text) < CenterY(header) + height * .12)
            .OrderBy(CenterY).ThenBy(text => text.Box.X);
        string boss = CanonicalBoss(Compact(string.Concat(nameLabels.Select(text => text.Text))));
        var typeLabels = labels.Where(text => text.Box.X > textLeft &&
            CenterY(text) > CenterY(header) + height * .12 && CenterY(text) < CenterY(header) + height * .4).ToList();
        // Anchor rows to the detected training labels; compact panels reduce the row spacing.
        var types = typeLabels.Where(t => Compact(t.Text).Contains("特训", StringComparison.Ordinal))
            .OrderBy(CenterY).Select(label =>
            {
                string line = string.Concat(typeLabels.Where(t => Math.Abs(CenterY(t) - CenterY(label)) <= label.Box.H * .65)
                    .OrderBy(t => t.Box.X).Select(t => Compact(t.Text)));
                return EnemyTypes.FirstOrDefault(type => line.EndsWith(type, StringComparison.Ordinal));
            }).OfType<string>().Distinct().Take(3).ToArray();

        return new(boss.Length > 0 ? boss : null, types, knownMap ?? _mapMatcher.Match(image), mode);
    }

    private static double CenterY(OcrTextResult text) => text.Box.Y + text.Box.H / 2.0;
    private static string Compact(string text) => Regex.Replace(text, @"\s+", "");

    private static string CanonicalBoss(string name) => name.Replace(':', '：') switch
    {
        "假想敌：统" or "假想敌：銃" => "假想敌：铳",
        "假想敌：鰭" => "假想敌：鳍",
        "萨米的意志" or "“萨米的意志”" or "\"萨米的意志\"" or "“萨光的意志”" or "“萨的意志”" => "“萨米的意志”",
        "阿利斯泰尔,帝国余晖" or "阿利斯泰尔，帝国余" or "阿利斯泰尔,帝国余" => "阿利斯泰尔，帝国余晖",
        var text => text
    };
}
