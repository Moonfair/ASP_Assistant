using ASPAssistant.Core.Models;
using ASPAssistant.Core.GameModes;

namespace ASPAssistant.Core.Services;

/// <summary>Recognizes shop name labels independently of card artwork and UI skins.</summary>
public sealed class ShopRecognizer(IOcrEngine ocr, IReadOnlyList<string>? catalogNames = null)
{
    public async Task<IReadOnlyList<ShopItem>> RecognizeAsync(byte[] png, int width, int height,
        IReadOnlyList<string> trackedNames, OcrRegionDefinition region)
    {
        if (width <= 0 || height <= 0 || trackedNames.Count == 0) return [];
        int top = (int)(Math.Max(region.YPercent, .90) * height);
        int bottom = (int)Math.Ceiling(Math.Min(region.YPercent + region.HeightPercent, .985) * height);
        if (bottom <= top) return [];
        var texts = await ocr.RecognizeRegionAsync(png,
            (int)(region.XPercent * width), top,
            (int)(region.WidthPercent * width), bottom - top);
        var tracked = trackedNames.ToHashSet(StringComparer.Ordinal);
        var names = (catalogNames ?? trackedNames).Concat(trackedNames)
            .GroupBy(Normalize).ToDictionary(g => g.Key, g => g.FirstOrDefault(tracked.Contains) ?? g.First());
        var items = new List<ShopItem>();
        var seen = new HashSet<string>();
        double scale = height / 720.0;
        var readable = texts.Where(t => IsNameRow(t, height) && names.ContainsKey(Normalize(t.Text))).ToList();
        // Known catalog labels establish the name baseline even when those cards
        // are not tracked. Do not reread faction rows, footer art or a known card's icon.
        double? baseline = readable.Count == 0 ? null
            : readable.Select(t => t.Box.Y + t.Box.H / 2.0).Order().ElementAt(readable.Count / 2);
        double rowTolerance = readable.Count == 0 ? 0 : readable.Max(t => t.Box.H) * .6;

        foreach (var text in texts.OrderBy(t => t.Box.X))
        {
            var box = text.Box;
            // Only the bottom name strip, not faction/trait labels or the battlefield.
            if (!IsNameRow(text, height)) continue;

            int nameX = box.X;
            if (!names.TryGetValue(Normalize(text.Text), out var name))
            {
                var normalized = Normalize(text.Text);
                if (normalized.StartsWith("收起", StringComparison.Ordinal) || normalized == "升级") continue;
                if (baseline.HasValue && Math.Abs(box.Y + box.H / 2.0 - baseline.Value) > rowTolerance) continue;
                if (readable.Any(t => box.X >= t.Box.X - 32 * scale
                    && box.X < t.Box.X + 132 * scale)) continue;
                // OCR can merge the profession icon into a label, or lose a character.
                // Reread the text after that square icon; never guess a partial name.
                // A narrow OCR strip can produce vertically padded boxes. In that
                // case use character width, so the icon crop does not eat the name.
                double glyphWidth = box.W / (double)Math.Max(1, normalized.Length);
                int iconWidth = box.H > glyphWidth * 1.3 ? (int)Math.Round(glyphWidth) : box.H;
                int retryX = Math.Clamp(box.X + iconWidth, 0, width - 1);
                int retryY = Math.Max(0, box.Y - 5);
                int w = Math.Min(box.W - iconWidth, width - retryX);
                int h = Math.Min(box.H + 10, height - retryY);
                if (w < box.H * .5 || h <= 0) continue;
                var reread = await ocr.RecognizeRegionAsync(png, retryX, retryY, w, h);
                var matches = reread.Where(t => names.ContainsKey(Normalize(t.Text))).ToList();
                if (matches.Count != 1) continue;
                name = names[Normalize(matches[0].Text)];
                nameX = matches[0].Box.X;
            }
            if (!tracked.Contains(name)) continue;

            var id = $"{name}@{box.X}";
            if (!seen.Add(id)) continue;
            // Name strips retain their position within the shop cards across skins.
            // Anchor the marker to that label using the 720p card dimensions; portraits,
            // border colours and cost badges no longer participate in recognition.
            double x = Math.Clamp(nameX - 32 * scale, 0, width - 1);
            double y = Math.Clamp(box.Y - 145 * scale, 0, height - 1);
            items.Add(new ShopItem
            {
                Id = id, Name = name,
                OcrRegion = (x / width, y / height,
                    Math.Min(164 * scale, width - x) / width,
                    Math.Min(184 * scale, height - y) / height)
            });
        }
        return items;
    }

    private static string Normalize(string text)
    {
        const string protocolSuffix = "(卫戍协议)";
        if (text.EndsWith(protocolSuffix, StringComparison.Ordinal))
            text = text[..^protocolSuffix.Length];
        return new(text.Where(char.IsLetterOrDigit).ToArray());
    }

    private static bool IsNameRow(OcrTextResult text, int height) => text.Box.W > 0 && text.Box.H > 0
        && text.Box.Y + text.Box.H / 2.0 >= height * .90
        && text.Box.Y + text.Box.H / 2.0 <= height * .985;
}
