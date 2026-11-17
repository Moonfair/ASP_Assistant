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
        int y = (int)(image.Height * .43);
        var labels = await ocr.RecognizeRegionAsync(png, x, y, image.Width - x, (int)(image.Height * .41));
        var header = labels.FirstOrDefault(text => Compact(text.Text).Contains("敌方领袖", StringComparison.Ordinal));
        if (header == null) return null;

        double height = image.Height;
        // The labels can wrap or be split by the mouse pointer.
        var nameLabels = labels.Where(text => text.Box.X > image.Width * .79 &&
            CenterY(text) > CenterY(header) + height * .026 && CenterY(text) < CenterY(header) + height * .12)
            .OrderBy(CenterY).ThenBy(text => text.Box.X);
        string boss = CanonicalBoss(Compact(string.Concat(nameLabels.Select(text => text.Text))));
        var types = Enumerable.Range(0, 3)
            .Select(row =>
            {
                double center = CenterY(header) + height * (.174 + row * .070);
                var line = string.Concat(labels.Where(text => text.Box.X > image.Width * .79 &&
                    Math.Abs(CenterY(text) - center) <= height * .029)
                    .OrderBy(text => text.Box.X).Select(text => Compact(text.Text)));
                return EnemyTypes.FirstOrDefault(type => line.EndsWith(type, StringComparison.Ordinal));
            }).OfType<string>().Distinct().ToArray();

        return new(boss.Length > 0 ? boss : null, types, knownMap ?? _mapMatcher.Match(image), mode);
    }

    private static double CenterY(OcrTextResult text) => text.Box.Y + text.Box.H / 2.0;
    private static string Compact(string text) => Regex.Replace(text, @"\s+", "");

    private static string CanonicalBoss(string name) => name.Replace(':', '：') switch
    {
        "假想敌：统" or "假想敌：銃" => "假想敌：铳",
        "假想敌：鰭" => "假想敌：鳍",
        "萨米的意志" or "“萨米的意志”" or "\"萨米的意志\"" => "“萨米的意志”",
        "阿利斯泰尔,帝国余晖" => "阿利斯泰尔，帝国余晖",
        var text => text
    };
}
