using ASPAssistant.Core.Models;
using ASPAssistant.Core.GameModes;
using System.Drawing;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

/// <summary>Recognizes shop name labels independently of card artwork and UI skins.</summary>
public sealed class ShopRecognizer(IOcrEngine ocr, IReadOnlyList<string>? catalogNames = null)
{
    public async Task<IReadOnlyList<ShopItem>> RecognizeAsync(byte[] png, int width, int height,
        IReadOnlyList<string> trackedNames, OcrRegionDefinition region, bool shopVisible = false)
    {
        if (width <= 0 || height <= 0 || trackedNames.Count == 0) return [];
        if (Math.Abs(width - height * 16d / 9) > 8 && ScreenCaptureService.GetPngSize(png) == (width, height))
        {
            using var input = new MemoryStream(png);
            using var image = new Bitmap(input);
            var viewport = GarrisonFrameGeometry.Viewport(image);
            if (viewport.Size != image.Size)
            {
                using var client = image.Clone(viewport, image.PixelFormat);
                var items = await ReadShopAsync(GarrisonFrameGeometry.Encode(client), client.Width, client.Height,
                    trackedNames, region, shopVisible);
                foreach (var item in items)
                {
                    var box = item.OcrRegion;
                    item.OcrRegion = ((viewport.X + box.X * client.Width) / width,
                        (viewport.Y + box.Y * client.Height) / height,
                        box.Width * client.Width / width, box.Height * client.Height / height);
                }
                return items;
            }
        }
        return await ReadShopAsync(png, width, height, trackedNames, region, shopVisible);
    }

    private async Task<IReadOnlyList<ShopItem>> ReadShopAsync(byte[] png, int width, int height,
        IReadOnlyList<string> trackedNames, OcrRegionDefinition region, bool shopVisible)
    {
        double scale = GarrisonFrameGeometry.UiScale(width, height);
        double nameTop = height - 72 * scale, nameBottom = height - 10.8 * scale;
        int top = (int)Math.Max(region.YPercent * height, nameTop);
        int bottom = (int)Math.Ceiling(Math.Min((region.YPercent + region.HeightPercent) * height, nameBottom));
        if (bottom <= top) return [];
        var texts = await ocr.RecognizeRegionAsync(png,
            (int)(region.XPercent * width), top,
            (int)(region.WidthPercent * width), bottom - top);
        var tracked = trackedNames.ToHashSet(StringComparer.Ordinal);
        var names = (catalogNames ?? trackedNames).Concat(trackedNames)
            .GroupBy(Normalize).ToDictionary(g => g.Key, g => g.FirstOrDefault(tracked.Contains) ?? g.First());
        var items = new List<ShopItem>();
        var seen = new HashSet<string>();
        var readable = texts.Where(t => IsNameRow(t, nameTop, nameBottom) && names.ContainsKey(Normalize(t.Text))).ToList();
        if (ocr is MaaOcrEngine native && readable.Count < 4 && (shopVisible || readable.Count > 0))
        {
            var recovered = RecoverMissingLabels(native, png, width, height, scale, readable, names);
            texts = texts.Concat(recovered).ToList();
            readable.AddRange(recovered);
        }
        // Known catalog labels establish the name baseline even when those cards
        // are not tracked. Do not reread faction rows, footer art or a known card's icon.
        double? baseline = readable.Count == 0 ? null
            : readable.Select(t => t.Box.Y + t.Box.H / 2.0).Order().ElementAt(readable.Count / 2);
        double rowTolerance = readable.Count == 0 ? 0 : readable.Max(t => t.Box.H) * .6;

        foreach (var text in texts.OrderBy(t => t.Box.X))
        {
            var box = text.Box;
            // Only the bottom name strip, not faction/trait labels or the battlefield.
            if (!IsNameRow(text, nameTop, nameBottom)) continue;

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

    // Small glyphs can disappear entirely in the line detector. Once a shop is visible,
    // read each missing card's complete name directly and keep only exact catalog entries.
    private static List<OcrTextResult> RecoverMissingLabels(MaaOcrEngine engine, byte[] png,
        int width, int height, double scale, List<OcrTextResult> readable, Dictionary<string, string> names)
    {
        var results = new List<OcrTextResult>();
        int sourceWidth = (int)Math.Round(1280 * scale), sourceHeight = (int)Math.Round(72 * scale);
        if (sourceHeight < 1 || sourceWidth < 1) return results;
        var strip = new Rectangle(width - sourceWidth, height - sourceHeight, sourceWidth, sourceHeight);
        using var input = new MemoryStream(png);
        using var original = new Bitmap(input);
        using var labels = GarrisonFrameGeometry.Resize(original, strip, new Rectangle(0, 0, 1280, 72));
        using var frame = new MaaImageBuffer();
        if (!frame.TrySetEncodedData(GarrisonFrameGeometry.Encode(labels))) return results;
        double baseline = readable.Count == 0 ? 28 : readable.Select(t =>
            (t.Box.Y + t.Box.H / 2.0 - strip.Top) / scale).Order().ElementAt(readable.Count / 2);
        // The terminal skin places its labels lower and slightly farther left.
        bool lower = baseline >= 32;
        for (int slot = 0; slot < 4; slot++)
        {
            int x = (lower ? 477 : 486) + (int)Math.Round(162.4 * slot);
            int sourceX = strip.Left + (int)Math.Round(x * scale);
            if (readable.Any(t => Math.Abs(t.Box.X - sourceX) < 36 * scale)) continue;
            int y = Math.Clamp((int)Math.Round(baseline) - 12, 3, 41);
            foreach (var area in new[] { new Rectangle(x, y, 117, 25),
                         new Rectangle(x + 1, y + 3, 116, 25), new Rectangle(x + 3, y, 112, 21) })
            {
                var text = engine.ReadHudText(frame, area, "^.+$");
                if (text == null || !names.ContainsKey(Normalize(text))) continue;
                results.Add(new(text, (sourceX, strip.Top + (int)Math.Round(y * scale),
                    (int)Math.Ceiling(Math.Min(text.Length * 20, area.Width) * scale), (int)Math.Ceiling(25 * scale))));
                break;
            }
        }
        return results;
    }

    private static string Normalize(string text)
    {
        const string protocolSuffix = "(卫戍协议)";
        if (text.EndsWith(protocolSuffix, StringComparison.Ordinal))
            text = text[..^protocolSuffix.Length];
        return new(text.Where(char.IsLetterOrDigit).ToArray());
    }

    private static bool IsNameRow(OcrTextResult text, double top, double bottom) => text.Box.W > 0 && text.Box.H > 0
        && text.Box.Y + text.Box.H / 2.0 >= top && text.Box.Y + text.Box.H / 2.0 <= bottom;
}
