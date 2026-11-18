using System.Drawing;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

internal static class GroupedCoreRecognizer
{
    internal sealed record Selection(int Side, IReadOnlyList<string>? Enabled);

    internal static Selection? Read(Bitmap image, IReadOnlyList<OcrTextResult> texts, IReadOnlyList<string> names,
        IReadOnlyList<Rectangle[]>? rows = null)
    {
        var additionalTitle = texts.FirstOrDefault(t => t.Text.Contains("附加盟"));
        if (additionalTitle == null || names.Count == 0 || names.Count % 2 != 0) return null;
        var measured = ReadMeasured(image, texts, names, additionalTitle, rows ?? CovenantIconLocator.FindRows(image));
        if (measured != null) return measured;
        double w = image.Width, h = image.Height;
        int top = additionalTitle.Box.Y;
        bool left = HasSelectionBar(image, top, .03, .33);
        bool right = HasSelectionBar(image, top, .37, .67);
        if (left == right) return null;

        // The heading and selection bar scroll together. Read both label rows relative to them.
        double start = left ? 0 : w * .35;
        double end = left ? w * .35 : w * .70;
        var enabled = new List<string>();
        foreach (var text in texts)
        {
            if (text.Box.X < start || text.Box.X + text.Box.W > end) continue;
            double fromHeading = (top - (text.Box.Y + text.Box.H / 2.0)) / h;
            if (Math.Abs(fromHeading - .255) > .027 && Math.Abs(fromHeading - .073) > .027) continue;
            var compact = Regex.Replace(text.Text, @"\s+", "");
            var matches = CovenantLabelMatcher.Find(compact, names).ToArray();
            if (matches.Sum(m => m.Length) == compact.Length)
                enabled.AddRange(matches.Select(m => m.Name));
        }

        return new(left ? 0 : 1, enabled.Count == names.Count / 2 && enabled.Distinct().Count() == enabled.Count
            ? enabled : null);
    }

    private static Selection? ReadMeasured(Bitmap image, IReadOnlyList<OcrTextResult> texts,
        IReadOnlyList<string> names, OcrTextResult title, IReadOnlyList<Rectangle[]> rows)
    {
        var groups = new[] { new List<Rectangle>(), new List<Rectangle>() };
        foreach (var row in rows.Where(row => row[0].Bottom < title.Box.Y))
        {
            var gaps = row.Zip(row.Skip(1), (a, b) => b.Left - a.Right).ToArray();
            if (gaps.Length == 0) continue;
            int split = Array.IndexOf(gaps, gaps.Max()) + 1;
            if (gaps[split - 1] < row[0].Width * 1.5 || split != row.Length - split) continue;
            int y = (int)Math.Round(row.Average(box => box.Y));
            for (int index = 0; index < row.Length; index++)
            {
                var box = row[index];
                groups[index < split ? 0 : 1].Add(new Rectangle(box.X, y, box.Width, box.Height));
            }
        }
        if (groups.Any(group => group.Count != names.Count / 2)) return null;
        var selected = groups.Select(group =>
        {
            int diameter = group.Max(box => box.Height);
            var area = Rectangle.Intersect(new Rectangle(Point.Empty, image.Size),
                Rectangle.FromLTRB(group.Min(box => box.Left) - (int)(diameter * 1.25),
                    group.Max(box => box.Bottom) + (int)(diameter * .35),
                    group.Max(box => box.Right) + (int)(diameter * 1.25), title.Box.Y - (int)(diameter * .1)));
            return HasSelectionBar(image, area);
        }).ToArray();
        if (selected[0] == selected[1]) return null;
        int side = selected[0] ? 0 : 1;
        var labels = AdaptiveCovenantRecognizer.ReadLabels(groups[side], names, texts);
        return new(side, labels?.Count == names.Count / 2 ? labels.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToArray() : null);
    }

    private static bool HasSelectionBar(Bitmap image, int headingTop, double left, double right)
    {
        int x0 = (int)(image.Width * left), x1 = (int)(image.Width * right);
        int y0 = Math.Max((int)(image.Height * .15), headingTop - (int)(image.Height * .055));
        int y1 = Math.Min(image.Height, headingTop - (int)(image.Height * .018));
        return HasSelectionBar(image, Rectangle.FromLTRB(x0, y0, x1, y1));
    }

    private static bool HasSelectionBar(Bitmap image, Rectangle area)
    {
        int matchingRows = 0;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            int green = 0, samples = 0;
            for (int x = area.Left; x < area.Right; x += 3)
            {
                var c = image.GetPixel(x, y);
                if (c.G > 125 && c.G - c.R > 55 && c.B - c.R > 40) green++;
                samples++;
            }
            if (samples > 0 && green >= samples * .72 && ++matchingRows >= 2) return true;
        }
        return false;
    }
}
