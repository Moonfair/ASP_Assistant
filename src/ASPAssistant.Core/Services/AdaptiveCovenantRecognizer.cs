using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

/// <summary>Reuses measured geometry while reading and validating the current frame's states and names.</summary>
internal sealed class AdaptiveCovenantRecognizer(IOcrEngine ocr)
{
    private CovenantIconLayout? _layout;

    internal void Reset() => _layout = null;

    internal CovenantIconLayout? Locate(Bitmap image, IReadOnlyList<OcrTextResult> texts, int cores, int additional)
    {
        if (_layout is { } cached && cached.Core.Length == cores && cached.Additional.Length == additional
            && cached.IsValid(image)) return cached;
        _layout = CovenantIconLayout.Locate(image, texts, cores, additional);
        return _layout;
    }

    internal static Rectangle TextArea(Bitmap image, IReadOnlyList<Rectangle> icons)
    {
        int diameter = icons.Max(box => box.Height);
        return Rectangle.Intersect(new Rectangle(Point.Empty, image.Size),
            Rectangle.FromLTRB(icons.Min(box => box.Left) - diameter / 2,
                icons.Min(box => box.Top) - diameter * 2,
                icons.Max(box => box.Right) + diameter / 2,
                icons.Max(box => box.Bottom) + diameter));
    }

    internal async Task<IReadOnlyList<string>?> ReadDisabledAsync(Bitmap image,
        IReadOnlyList<Rectangle> icons, IReadOnlyList<string> names, IReadOnlyList<OcrTextResult> texts)
    {
        if (icons.Count == 0) return [];
        var disabled = new List<int>();
        for (int index = 0; index < icons.Count; index++)
        {
            var state = CovenantIconClassifier.IsDisabled(image, icons[index]);
            if (state == null) return null;
            if (state.Value) disabled.Add(index);
        }
        if (disabled.Count == 0) return [];

        var labels = ReadLabels(icons, names, texts);
        if (labels != null && disabled.All(labels.ContainsKey))
            return disabled.Select(index => labels[index]).ToArray();

        // Each enlarged label has its own padded row. A bad merged box cannot shift names between icons.
        var reread = new Dictionary<int, string>();
        foreach (var batch in disabled.Chunk(6))
        {
            var crops = batch.Select(index => LabelBox(icons, index)).ToArray();
            if (crops.Any(crop => !new Rectangle(Point.Empty, image.Size).Contains(crop))) return null;
            int scale = Math.Max(2, (int)Math.Ceiling(32.0 / (icons[batch[0]].Height * .3)));
            int labelWidth = crops.Max(crop => crop.Width) * scale;
            int labelHeight = crops.Max(crop => crop.Height) * scale;
            int rowHeight = labelHeight + 24;
            using var strip = new Bitmap(labelWidth + 32, rowHeight * batch.Length);
            using (var graphics = Graphics.FromImage(strip))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                for (int row = 0; row < batch.Length; row++)
                    graphics.DrawImage(image, new Rectangle(16, row * rowHeight + 12,
                        crops[row].Width * scale, crops[row].Height * scale), crops[row], GraphicsUnit.Pixel);
            }
            var recognized = await ReadStripAsync(strip, rowHeight, names);
            if (recognized == null && icons[batch[0]].Height < 40)
            {
                // Color fringes on very small text can survive enlargement and confuse OCR.
                using var grayscale = new Bitmap(strip.Width, strip.Height);
                using (var graphics = Graphics.FromImage(grayscale))
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix([
                        [1f / 3, 1f / 3, 1f / 3, 0, 0],
                        [1f / 3, 1f / 3, 1f / 3, 0, 0],
                        [1f / 3, 1f / 3, 1f / 3, 0, 0],
                        [0, 0, 0, 1, 0],
                        [0, 0, 0, 0, 1]]));
                    graphics.DrawImage(strip, new Rectangle(Point.Empty, strip.Size), 0, 0, strip.Width, strip.Height,
                        GraphicsUnit.Pixel, attributes);
                }
                recognized = await ReadStripAsync(grayscale, rowHeight, names);
            }
            if (recognized == null) return null;
            for (int row = 0; row < batch.Length; row++)
                reread.Add(batch[row], recognized[row]);
        }
        return reread.Values.Distinct().Count() == disabled.Count
            ? disabled.Select(index => reread[index]).ToArray() : null;
    }

    private async Task<string[]?> ReadStripAsync(Bitmap strip, int rowHeight, IReadOnlyList<string> names)
    {
        using var output = new MemoryStream();
        strip.Save(output, ImageFormat.Png);
        var observed = await ocr.RecognizeRegionAsync(output.ToArray(), 0, 0, strip.Width, strip.Height);
        if (observed.Any(text => text.Box.Y < 0 || text.Box.Y + text.Box.H > strip.Height
            || text.Box.Y / rowHeight != (text.Box.Y + Math.Max(0, text.Box.H - 1)) / rowHeight)) return null;
        var labels = new string[strip.Height / rowHeight];
        for (int row = 0; row < labels.Length; row++)
        {
            var compact = Compact(string.Concat(observed.Where(text => text.Box.Y / rowHeight == row)
                .OrderBy(text => text.Box.X).Select(text => text.Text)));
            var matches = CovenantLabelMatcher.Find(compact, names).ToArray();
            if (matches.Length != 1 || matches[0].Index != 0 || matches[0].Length != compact.Length) return null;
            labels[row] = matches[0].Name;
        }
        return labels;
    }

    internal static Dictionary<int, string>? ReadLabels(IReadOnlyList<Rectangle> icons,
        IReadOnlyList<string> names, IReadOnlyList<OcrTextResult> texts)
    {
        var labels = new Dictionary<int, string>();
        foreach (var row in icons.Select((box, index) => (Box: box, Index: index)).GroupBy(item => item.Box.Y))
        {
            var slots = row.ToArray();
            double first = CovenantIconLocator.CenterX(slots[0].Box);
            double pitch = slots.Length > 1
                ? CovenantIconLocator.CenterX(slots[1].Box) - first : slots[0].Box.Width * 1.35;
            var grid = new CovenantGrid(first, pitch, pitch * .4, 0, 0, 0, 0);
            int top = slots[0].Box.Bottom, bottom = top + (int)(slots[0].Box.Height * .8);
            foreach (var text in texts.Where(text => text.Box.Y + text.Box.H / 2.0 > top
                && text.Box.Y + text.Box.H / 2.0 < bottom && text.Box.X >= first - pitch * .5
                && text.Box.X + text.Box.W <= grid.ColumnCenter(slots.Length - 1) + pitch * .5))
            {
                string compact = Compact(text.Text);
                var matches = CovenantLabelMatcher.Find(compact, names).ToArray();
                if (!CovenantLabelMatcher.HasCompleteSlotCoverage(text, grid, compact, matches)) return null;
                foreach (var match in matches)
                {
                    double x = text.Box.X + text.Box.W * (match.Index + match.Length / 2.0) / compact.Length;
                    int column = grid.NearestColumn(x);
                    if (column < 0 || column >= slots.Length || Math.Abs(x - grid.ColumnCenter(column)) > grid.ColumnTolerance
                        || !labels.TryAdd(slots[column].Index, match.Name)) return null;
                }
            }
        }
        return labels.Values.Distinct().Count() == labels.Count ? labels : null;
    }

    private static Rectangle LabelBox(IReadOnlyList<Rectangle> icons, int index)
    {
        var box = icons[index];
        double pitch = icons.Where(other => other.Y == box.Y && other.X != box.X)
            .Select(other => Math.Abs(CovenantIconLocator.CenterX(other) - CovenantIconLocator.CenterX(box)))
            .DefaultIfEmpty(box.Width * 1.35).Min();
        return new Rectangle((int)Math.Round(CovenantIconLocator.CenterX(box) - pitch * .49),
            box.Bottom + (int)(box.Height * .04), (int)Math.Round(pitch * .98), (int)Math.Ceiling(box.Height * .60));
    }

    private static string Compact(string text) => Regex.Replace(text, @"\s+", "");
}
