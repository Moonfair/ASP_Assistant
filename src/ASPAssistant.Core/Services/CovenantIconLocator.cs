using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Services;

/// <summary>Finds covenant rings and measures each row independently of the capture dimensions.</summary>
internal static class CovenantIconLocator
{
    internal static Rectangle? FindTextArea(Bitmap image, IReadOnlyList<Rectangle[]> rows)
    {
        var uniform = rows.Where(row => row.Min(box => box.Height) >= row.Max(box => box.Height) * .8).ToArray();
        int diameter = uniform.SelectMany(row => row).Select(box => box.Height).DefaultIfEmpty(0).Max();
        var icons = uniform.Where(row => row[0].Height >= diameter * .7).SelectMany(row => row).ToArray();
        return icons.Length == 0 ? null : Rectangle.Intersect(new Rectangle(Point.Empty, image.Size),
            Rectangle.FromLTRB(icons.Min(box => box.Left) - diameter / 2, icons.Min(box => box.Top) - diameter * 2,
                icons.Max(box => box.Right) + diameter / 2, icons.Max(box => box.Bottom) + diameter));
    }

    internal static IReadOnlyList<Rectangle[]> FindRows(Bitmap source)
    {
        double scale = Math.Min(1, 1280.0 / Math.Max(source.Width, source.Height));
        using var image = new Bitmap(Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)), PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
            graphics.DrawImage(source, new Rectangle(Point.Empty, image.Size),
                new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);

        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * data.Height];
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
        finally { image.UnlockBits(data); }
        int width = image.Width, height = image.Height;
        var mask = new bool[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = y * data.Stride + x * 4;
            int b = pixels[p], g = pixels[p + 1], r = pixels[p + 2];
            int minimum = Math.Min(r, Math.Min(g, b));
            int maximum = Math.Max(r, Math.Max(g, b));
            mask[y * width + x] = (g > 85 && g - r > 35 && b - r > 20)
                || (minimum > 60 && maximum - minimum < 60);
        }

        var candidates = new List<Rectangle>();
        var queue = new Queue<int>();
        for (int index = 0; index < mask.Length; index++)
        {
            if (!mask[index]) continue;
            mask[index] = false;
            queue.Enqueue(index);
            int left = index % width, right = left, top = index / width, bottom = top, count = 0;
            while (queue.TryDequeue(out int point))
            {
                int x = point % width, y = point / width;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                count++;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int next = ny * width + nx;
                    if (!mask[next]) continue;
                    mask[next] = false;
                    queue.Enqueue(next);
                }
            }

            int w = right - left + 1, h = bottom - top + 1;
            var box = new Rectangle(left, top, w, h);
            if (w >= 12 && h >= 12 && w <= 180 && h <= 180
                && w >= h * .85 && w <= h * 1.18 && count >= w * h * .15 && IsRing(image, box))
                candidates.Add(box);
        }

        var outer = candidates.Where(box => !candidates.Any(other => other != box && other.Contains(box)))
            .OrderBy(box => box.Top + box.Height / 2.0).ToArray();
        var rows = new List<List<Rectangle>>();
        foreach (var box in outer)
        {
            var row = rows.LastOrDefault();
            if (row == null || Math.Abs(CenterY(box) - row.Average(CenterY)) > box.Height * .22)
            {
                row = [];
                rows.Add(row);
            }
            row.Add(box);
        }

        return rows.Where(row => row.Count >= 3).Select(row => row.OrderBy(box => box.X)
            .Select(box => Rectangle.FromLTRB((int)Math.Round(box.Left / scale), (int)Math.Round(box.Top / scale),
                (int)Math.Round(box.Right / scale), (int)Math.Round(box.Bottom / scale))).ToArray()).ToArray();
    }

    internal static Rectangle[]? CompleteRow(IReadOnlyList<Rectangle> row, int count)
    {
        if (count < 1 || row.Count < Math.Max(3, count - 1)) return null;
        double diameter = Median(row.Select(box => (double)box.Width));
        var similar = row.Where(box => Math.Abs(box.Width - diameter) <= diameter * .18).OrderBy(box => box.X).ToArray();
        if (similar.Length < Math.Max(3, count - 1)) return null;
        var gaps = similar.Zip(similar.Skip(1), (a, b) => CenterX(b) - CenterX(a))
            .Where(gap => gap > diameter).ToArray();
        if (gaps.Length == 0) return null;
        double pitch = Median(gaps.Select(gap => gap / Math.Max(1, Math.Round(gap / (diameter * 1.35)))));
        if (pitch < diameter * 1.15 || pitch > diameter * 1.8) return null;

        double first = CenterX(similar[0]), y = Median(similar.Select(CenterY));
        var occupied = new HashSet<int>();
        foreach (var box in similar)
        {
            int column = (int)Math.Round((CenterX(box) - first) / pitch);
            if (column < 0 || column >= count || !occupied.Add(column)
                || Math.Abs(CenterX(box) - first - column * pitch) > diameter * .2) return null;
        }
        // Interior occlusion can be checked at its measured neighbors' spacing.
        if (!occupied.Contains(count - 1)) return null;
        return Enumerable.Range(0, count).Select(column =>
            new Rectangle((int)Math.Round(first + column * pitch - diameter / 2),
                (int)Math.Round(y - diameter / 2), (int)Math.Round(diameter), (int)Math.Round(diameter))).ToArray();
    }

    internal static bool IsRing(Bitmap image, Rectangle box)
    {
        int hits = 0;
        for (int i = 0; i < 16; i++)
        {
            double angle = i * Math.PI / 8;
            double cx = CenterX(box), cy = CenterY(box);
            var inside = Sample(.44);
            var outside = Sample(.62);
            if (inside.HasValue && outside.HasValue && inside.Value > 55 && inside.Value - outside.Value > 8) hits++;

            double? Sample(double radius)
            {
                int x = (int)Math.Round(cx + box.Width * radius * Math.Cos(angle));
                int y = (int)Math.Round(cy + box.Height * radius * Math.Sin(angle));
                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return null;
                var color = image.GetPixel(x, y);
                return (color.R + color.G + color.B) / 3.0;
            }
        }
        return hits >= 11;
    }

    internal static double CenterX(Rectangle box) => box.X + box.Width / 2.0;
    internal static double CenterY(Rectangle box) => box.Y + box.Height / 2.0;
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }
}

internal sealed record CovenantIconLayout(Size ImageSize, Rectangle[] Core, Rectangle[] Additional)
{
    internal bool IsValid(Bitmap image) => image.Size == ImageSize &&
        Core.Concat(Additional).All(box => CovenantIconLocator.IsRing(image, box));

    internal static CovenantIconLayout? Locate(Bitmap image, IReadOnlyList<OcrTextResult> texts,
        int coreCount, int additionalCount)
    {
        var rows = CovenantIconLocator.FindRows(image);
        var coreTitle = texts.FirstOrDefault(t => t.Text.Contains("核心盟"));
        var additionalTitle = texts.FirstOrDefault(t => t.Text.Contains("附加盟"));
        var banTitle = texts.FirstOrDefault(t => t.Text.Contains("本局禁用干员"));
        if (coreTitle == null && additionalTitle == null) return null;
        if (coreCount == 0)
        {
            if (additionalTitle == null) return null;
            var extra = FindAdditional(additionalTitle.Box.Y + additionalTitle.Box.H);
            return extra == null ? null : new(image.Size, [], extra);
        }
        var coreRows = rows.Where(row =>
            (coreTitle == null || (row[0].Top > coreTitle.Box.Y + coreTitle.Box.H &&
                row[0].Top < coreTitle.Box.Y + coreTitle.Box.H + row[0].Width * 2)) &&
            (additionalTitle == null || row[0].Bottom < additionalTitle.Box.Y));
        foreach (var row in coreRows)
        {
            var core = CovenantIconLocator.CompleteRow(row, coreCount);
            if (core == null) continue;
            var additional = FindAdditional(Math.Max(core[0].Bottom,
                additionalTitle == null ? 0 : additionalTitle.Box.Y + additionalTitle.Box.H));
            if (additional != null) return new(image.Size, core, additional);
        }
        return null;

        Rectangle[]? FindAdditional(int top)
        {
            var additional = new List<Rectangle>();
            foreach (var extraRow in rows.Where(r => r[0].Top > top &&
                (banTitle == null || r[0].Bottom < banTitle.Box.Y)))
            {
                int remaining = additionalCount - additional.Count;
                if (remaining <= 0) break;
                var slots = CovenantIconLocator.CompleteRow(extraRow, Math.Min(10, remaining));
                if (slots == null) continue;
                if (additional.Count > 0 && (Math.Abs(CovenantIconLocator.CenterX(slots[0])
                    - CovenantIconLocator.CenterX(additional[0])) > additional[0].Width * .3 ||
                    Math.Abs(slots[0].Width - additional[0].Width) > additional[0].Width * .2)) continue;
                additional.AddRange(slots);
            }
            return additional.Count == additionalCount ? additional.ToArray() : null;
        }
    }
}
