using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ASPAssistant.Core.Services;

/// <summary>Locates red ban badges and verifies the adjoining portrait border.</summary>
public static class BanCardLocator
{
    // Layout coordinates use the game's 1920x1080 reference canvas, not desktop pixels.
    public static IReadOnlyList<Rectangle> FindCards(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var source = new Bitmap(stream);
        using var canvas = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas)) g.DrawImage(source, 0, 0, 1920, 1080);
        var bitmapData = canvas.LockBits(new Rectangle(0, 0, 1920, 1080), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[bitmapData.Stride * 1080];
        Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
        canvas.UnlockBits(bitmapData);
        var mask = new bool[1920 * 1080];
        for (int y = 180; y < 1035; y++)
        for (int x = 250; x < 1330; x++)
        {
            int p = y * bitmapData.Stride + x * 4;
            int b = pixels[p], green = pixels[p + 1], red = pixels[p + 2];
            mask[y * 1920 + x] = red > 120 && red > green * 1.55 && Math.Abs(green - b) < 30;
        }

        bool Pale(int x, int y)
        {
            int p = y * bitmapData.Stride + x * 4;
            int min = Math.Min(pixels[p], Math.Min(pixels[p + 1], pixels[p + 2]));
            int max = Math.Max(pixels[p], Math.Max(pixels[p + 1], pixels[p + 2]));
            return min > 130 && max - min < 65;
        }

        var cards = new List<Rectangle>();
        var queue = new Queue<int>();
        for (int y = 180; y < 1035; y++)
        for (int x = 250; x < 1330; x++)
        {
            int index = y * 1920 + x;
            if (!mask[index]) continue;
            mask[index] = false;
            queue.Enqueue(index);
            int left = x, right = x, top = y, bottom = y, count = 0;
            while (queue.TryDequeue(out int point))
            {
                int px = point % 1920, py = point / 1920;
                left = Math.Min(left, px); right = Math.Max(right, px);
                top = Math.Min(top, py); bottom = Math.Max(bottom, py); count++;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = px + dx, ny = py + dy;
                    if (nx < 250 || nx >= 1330 || ny < 180 || ny >= 1035) continue;
                    int next = ny * 1920 + nx;
                    if (!mask[next]) continue;
                    mask[next] = false;
                    queue.Enqueue(next);
                }
            }
            int w = right - left + 1, h = bottom - top + 1;
            if (w < 11 || w > 28 || h < 23 || h > 37 || count < 85) continue;
            int cardX = left - 106, cardY = top + 2;
            if (cardX < 245 || cardX + 130 > 1330 || cardY < 180 || cardY + 130 > 1035) continue;
            int paleTop = 0, paleLeft = 0;
            for (int d = 0; d < 3; d++)
            {
                for (int xx = 0; xx < 80; xx++) if (Pale(cardX + xx, cardY + d)) paleTop++;
                for (int yy = 0; yy < 95; yy++) if (Pale(cardX + d, cardY + yy)) paleLeft++;
            }
            // Red hair and enemy artwork do not have this portrait frame beside the badge.
            if (paleTop < 96 || paleLeft < 114) continue;
            var box = new Rectangle(
                (int)Math.Round(cardX * source.Width / 1920.0),
                (int)Math.Round(cardY * source.Height / 1080.0),
                (int)Math.Round(130 * source.Width / 1920.0),
                (int)Math.Round(130 * source.Height / 1080.0));
            if (!cards.Any(c => Math.Abs(c.X - box.X) < 5 && Math.Abs(c.Y - box.Y) < 5)) cards.Add(box);
        }
        return cards.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
    }

    public static string Fingerprint(byte[] png, IReadOnlyList<Rectangle> cards)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        var values = new List<byte>();
        foreach (var card in cards)
        {
            values.AddRange(BitConverter.GetBytes(card.X));
            values.AddRange(BitConverter.GetBytes(card.Y));
            for (int y = 2; y < 8; y++)
            for (int x = 1; x < 7; x++)
            {
                var color = bitmap.GetPixel(card.X + card.Width * x / 8, card.Y + card.Height * y / 8);
                values.Add((byte)((color.R + color.G + color.B) / 3 / 32));
            }
        }
        return Convert.ToHexString(SHA256.HashData(values.ToArray()));
    }

    /// <summary>White incomplete-roster markers identify group starts even if label OCR misses.</summary>
    public static IReadOnlyList<int> FindGroupStarts(byte[] png, IReadOnlyList<Rectangle> cards)
    {
        using var stream = new MemoryStream(png);
        using var source = new Bitmap(stream);
        using var canvas = new Bitmap(source, 1920, 1080);
        double PaleRatio(Rectangle region)
        {
            int pale = 0;
            for (int y = region.Top; y < region.Bottom; y++)
            for (int x = region.Left; x < region.Right; x++)
            {
                var color = canvas.GetPixel(x, y);
                int min = Math.Min(color.R, Math.Min(color.G, color.B));
                int max = Math.Max(color.R, Math.Max(color.G, color.B));
                if (min > 155 && max - min < 45) pale++;
            }
            return pale / (double)(region.Width * region.Height);
        }
        var starts = new List<int>();
        foreach (int y in cards.Select(c => c.Y).Distinct())
        {
            int top = (int)Math.Round(y * 1080.0 / source.Height);
            if (top < 22) continue;
            if (PaleRatio(new(163, top - 9, 16, 18)) > .45 &&
                PaleRatio(new(166, top - 22, 10, 10)) > .30 &&
                PaleRatio(new(184, top - 22, 10, 10)) > .20)
                starts.Add(y);
        }
        return starts.Order().ToList();
    }
}
