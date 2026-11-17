using System.Drawing;
using System.Drawing.Drawing2D;

namespace ASPAssistant.Core.Services;

public sealed class PreparationMapMatcher(string directory)
{
    private const int SampleWidth = 40;
    private const int SampleHeight = 24;
    private readonly Lazy<IReadOnlyList<MapTemplate>> _templates = new(() => LoadTemplates(directory));

    public int? Match(Bitmap screenshot)
    {
        var templates = _templates.Value;
        if (templates.Count == 0) return null;

        using var scaled = new Bitmap(SampleWidth, SampleHeight);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        var scores = new Dictionary<int, double>();
        // Account for the thumbnail border and small client-window scaling differences.
        foreach (double left in new[] { .720, .723, .726 })
        foreach (double top in new[] { .176, .181, .186 })
        foreach (double width in new[] { .255, .260, .265 })
        {
            var area = new RectangleF((float)(screenshot.Width * left), (float)(screenshot.Height * top),
                (float)(screenshot.Width * width), (float)(screenshot.Height * .260));
            graphics.DrawImage(screenshot, new Rectangle(0, 0, SampleWidth, SampleHeight),
                area.X, area.Y, area.Width, area.Height, GraphicsUnit.Pixel);
            var pixels = ReadInterior(scaled, 0);
            foreach (var template in templates)
            {
                int difference = 0;
                for (int i = 0; i < pixels.Length; i++)
                    difference += Math.Abs(pixels[i] - template.Pixels[i]);
                double score = difference / (255.0 * pixels.Length);
                if (score < scores.GetValueOrDefault(template.Battlefield, double.MaxValue))
                    scores[template.Battlefield] = score;
            }
        }

        var best = scores.OrderBy(entry => entry.Value).Take(2).ToArray();
        return best.Length == 2 && best[0].Value <= .065 && best[1].Value - best[0].Value >= .012
            ? best[0].Key : null;
    }

    private static IReadOnlyList<MapTemplate> LoadTemplates(string directory)
    {
        var templates = new List<MapTemplate>();
        foreach (int battlefield in BossRoutes.Battlefields)
        {
            string path = Path.Combine(directory, $"{battlefield:D2}.png");
            if (!File.Exists(path)) continue;
            try
            {
                using var strip = new Bitmap(path);
                if (strip.Width != 3 * SampleWidth || strip.Height != SampleHeight) continue;
                for (int field = 0; field < 3; field++)
                    templates.Add(new(battlefield, ReadInterior(strip, field * SampleWidth)));
            }
            catch (ArgumentException ex)
            {
                AppLogger.Error("Preparation", $"Invalid battlefield template {path}", ex);
            }
        }
        return templates;
    }

    private static byte[] ReadInterior(Bitmap image, int offsetX)
    {
        var pixels = new byte[30 * 16 * 3];
        int index = 0;
        // Exclude the frame, the zoom button and the edges of the preview.
        for (int y = 4; y < 20; y++)
        for (int x = 5; x < 35; x++)
        {
            var color = image.GetPixel(offsetX + x, y);
            pixels[index++] = color.R;
            pixels[index++] = color.G;
            pixels[index++] = color.B;
        }
        return pixels;
    }

    private sealed record MapTemplate(int Battlefield, byte[] Pixels);
}
