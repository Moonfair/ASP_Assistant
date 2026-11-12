using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

/// <summary>Reads every core icon state and requires readable names for disabled icons.</summary>
public static class CoreCovenantRecognizer
{
    public static IReadOnlyList<string>? FindDisabled(Bitmap image, IReadOnlyList<OcrTextResult> texts,
        IEnumerable<string> coreNames) =>
        FindDisabled(image, texts, coreNames, CovenantGrid.Preparation(image.Width, image.Height));

    internal static IReadOnlyList<string>? FindDisabled(Bitmap image, IReadOnlyList<OcrTextResult> texts,
        IEnumerable<string> coreNames, CovenantGrid grid)
    {
        var names = coreNames.Distinct().OrderByDescending(n => n.Length).ToArray();
        if (names.Length is < 1 or > 10) return null;
        double h = image.Height;
        var section = FindSection(texts, h);
        if (section == null) return null;
        int coreTop = section.Value.CoreTitle.Box.Y;
        int additionalTop = section.Value.AdditionalTitle.Box.Y;
        double sectionHeight = additionalTop - coreTop;

        var slots = new Dictionary<int, (string Name, double LabelY)>();
        foreach (var text in texts.Where(t => t.Box.Y > coreTop + sectionHeight * .55
            && t.Box.Y + t.Box.H < additionalTop))
        {
            // Maa may join several names into one box. Recover each name's approximate
            // center from its character span, then snap it to the ten fixed UI slots.
            var compact = Regex.Replace(text.Text, @"\s+", "");
            var matches = CovenantLabelMatcher.Find(compact, names).ToArray();
            if (!CovenantLabelMatcher.HasCompleteSlotCoverage(text, grid, compact, matches)) return null;
            foreach (var match in matches)
            {
                double x = text.Box.X + text.Box.W * (match.Index + match.Length / 2.0) / compact.Length;
                int slot = grid.NearestColumn(x);
                if (slot < 0 || slot >= names.Length || Math.Abs(x - grid.ColumnCenter(slot)) > grid.ColumnTolerance)
                    return null;
                if (!slots.TryAdd(slot, (match.Name, text.Box.Y + text.Box.H / 2.0))) return null;
            }
        }
        if (slots.Count == 0 || slots.Values.Select(s => s.Name).Distinct().Count() != slots.Count) return null;
        double labelY = slots.Values.Average(s => s.LabelY);
        if (slots.Values.Any(s => Math.Abs(s.LabelY - labelY) > h * .018)) return null;
        int cy = (int)Math.Round(labelY - grid.IconToLabel);
        var disabled = new List<string>();
        for (int slot = 0; slot < names.Length; slot++)
        {
            int cx = (int)Math.Round(grid.ColumnCenter(slot));
            var state = CovenantIconClassifier.IsDisabled(image, cx, cy);
            if (state == null) return null;
            // A misspelled label on a positively green icon cannot affect the banned set.
            // Inspect every icon in this mode, and never guess a missing gray icon's name.
            if (state.Value)
            {
                if (!slots.TryGetValue(slot, out var label)) return null;
                disabled.Add(label.Name);
            }
        }
        return disabled;
    }

    internal static async Task<IReadOnlyList<string>?> RereadDisabledAsync(Bitmap image,
        IReadOnlyList<OcrTextResult> texts, IReadOnlyList<string> names, IOcrEngine ocr, CovenantGrid grid)
    {
        if (names.Count is < 1 or > 10) return null;
        var section = FindSection(texts, image.Height);
        if (section == null) return null;

        // The additional heading anchors the label row even when OCR has merged its names.
        int labelY = (int)Math.Round(section.Value.AdditionalTitle.Box.Y - grid.CoreLabelToAdditionalTitle);
        int halfWidth = (int)Math.Round(grid.ColumnSpacing * (.033 / .0671875));
        int halfHeight = (int)Math.Round(image.Height * .026);
        var crops = new List<Rectangle>();
        for (int slot = 0; slot < names.Count; slot++)
        {
            int x = (int)Math.Round(grid.ColumnCenter(slot));
            var disabled = CovenantIconClassifier.IsDisabled(image, x, labelY - grid.IconToLabel);
            if (disabled == null) return null;
            if (!disabled.Value) continue;

            var crop = new Rectangle(x - halfWidth, labelY - halfHeight, 2 * halfWidth, 2 * halfHeight);
            if (!new Rectangle(Point.Empty, image.Size).Contains(crop) || crop.Width <= 0 || crop.Height <= 0)
                return null;
            crops.Add(crop);
        }
        if (crops.Count == 0) return [];

        // Give each label its own padded row so the detector cannot join neighboring names.
        const int scale = 2, horizontalPadding = 16, verticalPadding = 12;
        int labelWidth = crops[0].Width * scale;
        int labelHeight = crops[0].Height * scale;
        int rowHeight = labelHeight + verticalPadding * 2;
        using var strip = new Bitmap(labelWidth + horizontalPadding * 2, rowHeight * crops.Count);
        using (var graphics = Graphics.FromImage(strip))
        {
            graphics.Clear(Color.Black);
            for (int row = 0; row < crops.Count; row++)
                graphics.DrawImage(image, new Rectangle(horizontalPadding, row * rowHeight + verticalPadding,
                    labelWidth, labelHeight), crops[row], GraphicsUnit.Pixel);
        }
        using var stream = new MemoryStream();
        strip.Save(stream, ImageFormat.Png);
        var reread = await ocr.RecognizeRegionAsync(stream.ToArray(), 0, 0, strip.Width, strip.Height);

        var labels = new SortedDictionary<int, string>();
        foreach (var text in reread)
        {
            int row = text.Box.Y / rowHeight;
            if (text.Box.Y < 0 || row >= crops.Count || text.Box.Y + text.Box.H > (row + 1) * rowHeight)
                return null;
            var name = Regex.Replace(text.Text, @"\s+", "");
            if (!names.Contains(name) || !labels.TryAdd(row, name)) return null;
        }
        return labels.Count == crops.Count && labels.Values.Distinct().Count() == labels.Count
            ? labels.Values.ToArray() : null;
    }

    internal static (OcrTextResult CoreTitle, OcrTextResult AdditionalTitle)? FindSection(
        IReadOnlyList<OcrTextResult> texts, double height)
    {
        // The pointer can obscure the end of the core heading.
        var coreTitle = texts.FirstOrDefault(t => t.Text.Contains("核心盟"));
        var additionalTitle = texts.FirstOrDefault(t => t.Text.Contains("附加盟约"));
        if (coreTitle == null || additionalTitle == null) return null;
        double sectionHeight = additionalTitle.Box.Y - coreTitle.Box.Y;
        return sectionHeight >= height * .18 && sectionHeight <= height * .38
            ? (coreTitle, additionalTitle) : null;
    }
}

internal static class CovenantIconClassifier
{
    internal static bool? IsDisabled(Bitmap image, double centerX, double centerY)
    {
        double w = image.Width, h = image.Height;
        int cx = (int)Math.Round(centerX), cy = (int)Math.Round(centerY);
        var icon = Rectangle.FromLTRB(cx - (int)(w * .022), cy - (int)(h * .039),
            cx + (int)(w * .022), cy + (int)(h * .039));
        double cyan = Fraction(image, icon, c => c.G > 100 && c.G - c.R > 45 && c.B - c.R > 25);
        if (cyan >= .18) return false;
        double gray = Fraction(image, icon, c => Math.Min(c.R, Math.Min(c.G, c.B)) > 80
            && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) < 65);
        var badge = Rectangle.FromLTRB(cx + (int)(w * .013), cy - (int)(h * .060),
            cx + (int)(w * .034), cy - (int)(h * .019));
        double white = Fraction(image, badge, c => Math.Min(c.R, Math.Min(c.G, c.B)) > 140
            && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) < 50);
        return cyan < .04 && gray > .07 && white > .07 ? true : null;
    }

    private static double Fraction(Bitmap image, Rectangle region, Func<Color, bool> predicate)
    {
        if (!new Rectangle(0, 0, image.Width, image.Height).Contains(region) || region.Width <= 0 || region.Height <= 0)
            return 0;
        int hits = 0, total = 0;
        for (int y = region.Top; y < region.Bottom; y += 2)
        for (int x = region.Left; x < region.Right; x += 2)
        {
            total++;
            if (predicate(image.GetPixel(x, y))) hits++;
        }
        return total == 0 ? 0 : (double)hits / total;
    }
}
