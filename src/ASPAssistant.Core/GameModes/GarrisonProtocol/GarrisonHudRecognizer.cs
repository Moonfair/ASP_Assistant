using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.GameModes.GarrisonProtocol;

public readonly record struct GarrisonHud(int? Round, int? ShopLevel, int? Gold, int? UpgradeCost = null)
{
    public int? NextRoundGold => Round + 4;
}

public sealed class GarrisonHudRecognizer(MaaOcrEngine recognition)
{
    private const int ReferenceWidth = 1280;
    private const int ReferenceHeight = 720;
    private const string PositiveNumber = "^[1-9][0-9]?$";
    private const string GoldNumber = "^(0|[1-9][0-9]{0,2})$";
    private const string RomanNumber = "^[IVivl1/|ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ]{1,4}$";

    private enum LevelGlyph { LightRoman, DarkRoman, Arabic }

    private readonly record struct ShopPreset(string Template, double Threshold, Rectangle Level,
        Point BadgeOffset, LevelGlyph Glyph);

    private static readonly ShopPreset[] ShopPresets =
    [
        new("level-light", .84, new Rectangle(0, 18, 49, 28), new Point(5, -38), LevelGlyph.DarkRoman),
        new("level-modern", .82, new Rectangle(5, 24, 65, 61), new Point(20, -26), LevelGlyph.LightRoman),
        new("level-classic", .84, new Rectangle(-9, 13, 60, 49), new Point(2, -48), LevelGlyph.Arabic)
    ];

    public Task<GarrisonHud> RecognizeAsync(byte[] png) => Task.Run(() =>
    {
        try
        {
            return Recognize(png);
        }
        catch (Exception ex)
        {
            AppLogger.Error("GarrisonHud", "HUD recognition failed", ex);
            return default;
        }
    });

    private GarrisonHud Recognize(byte[] png)
    {
        if (ScreenCaptureService.GetPngSize(png).Width <= 0) return default;
        using var input = new MemoryStream(png);
        using var source = new Bitmap(input);
        var sourceBounds = new Rectangle(Point.Empty, source.Size);
        var viewport = FindViewport(source, sourceBounds);
        int normalizedWidth = (int)Math.Round(viewport.Width * (double)ReferenceHeight / viewport.Height);
        if (normalizedWidth < 900 || normalizedWidth > 2200) return default;
        var bounds = new Rectangle(0, 0, normalizedWidth, ReferenceHeight);
        using var normalized = viewport == sourceBounds && viewport.Size == bounds.Size
            ? null : Resize(source, viewport, bounds);

        using var frame = new MaaImageBuffer();
        var pixels = normalized == null ? png : Encode(normalized);
        if (!frame.TrySetEncodedData(pixels)) return default;

        // Match the in-game label first so shop illustrations in menus cannot supply live values.
        var roundArea = new Rectangle(normalizedWidth / 2 - 280, 10, 200, 50);
        var roundAnchor = recognition.FindHudTemplate(frame, roundArea, .78, ["round-modern", "round-classic"]);
        if (roundAnchor is not { } round) return default;
        // The two label variants can align a couple of pixels apart after downsampling.
        int? roundValue = ReadNumber(frame, bounds, round, new Rectangle(35, -1, 44, 18), PositiveNumber)
            ?? ReadNumber(frame, bounds, round, new Rectangle(36, 1, 44, 18), PositiveNumber);

        var goldArea = new Rectangle(normalizedWidth - 210, 490, 204, 160);
        var goldAnchor = recognition.FindHudTemplate(frame, goldArea, .83, ["gold-modern", "gold-classic"]);
        int? gold = goldAnchor is { } coin
            ? ReadNumber(frame, bounds, coin, new Rectangle(26, 0, 42, 22), GoldNumber) : null;

        var levelArea = new Rectangle(Math.Max(0, normalizedWidth - 1090), 510, 350, 140);
        foreach (var preset in ShopPresets)
        {
            if (recognition.FindHudTemplate(frame, levelArea, preset.Threshold, [preset.Template]) is not { } anchor)
                continue;
            int? level = preset.Glyph == LevelGlyph.Arabic
                ? ReadNumber(frame, bounds, anchor, preset.Level, "^[1-9]$")
                : ReadRoman(frame, normalized ?? source, bounds, anchor, preset);
            int? cost = ReadUpgradeCost(frame, bounds, anchor, preset.BadgeOffset);
            return new GarrisonHud(roundValue, level, gold, cost);
        }
        return new GarrisonHud(roundValue, null, gold);
    }

    private int? ReadUpgradeCost(MaaImageBuffer frame, Rectangle bounds, Rectangle anchor, Point offset)
    {
        // Match only the badge rim. The changing price is excluded from the template.
        var search = new Rectangle(anchor.X + offset.X - 8, anchor.Y + offset.Y - 6, 52, 39);
        if (!bounds.Contains(search)) return null;
        var badge = recognition.FindHudTemplate(frame, search, .72, ["upgrade-badge"], greenMask: true);
        return badge is { } price
            ? ReadNumber(frame, bounds, price, new Rectangle(6, 3, 24, 21), GoldNumber) : null;
    }

    private int? ReadNumber(MaaImageBuffer frame, Rectangle bounds, Rectangle anchor, Rectangle relativeArea, string expected)
    {
        relativeArea.Offset(anchor.Location);
        if (!bounds.Contains(relativeArea)) return null;
        string? text = recognition.ReadHudText(frame, relativeArea, expected);
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    private int? ReadRoman(MaaImageBuffer frame, Bitmap bitmap, Rectangle bounds, Rectangle anchor, ShopPreset preset)
    {
        var area = preset.Level;
        area.Offset(anchor.Location);
        if (!bounds.Contains(area)) return null;
        if (ReadLevelStrokes(bitmap, area, preset.Glyph == LevelGlyph.DarkRoman) is { } strokes)
            return strokes;

        var text = recognition.ReadHudText(frame, area, RomanNumber)?.Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant().Replace('1', 'I').Replace('L', 'I').Replace('/', 'I').Replace('|', 'I');
        return text switch
        {
            "I" => 1, "II" => 2, "III" => 3, "IV" => 4, "V" => 5,
            "VI" => 6, "VII" => 7, "VIII" => 8, "IX" => 9, "X" => 10,
            _ => null
        };
    }

    // The dark glyph fades out at the bottom, so classify from the rows where the strokes remain visible.
    private static int? ReadLevelStrokes(Bitmap bitmap, Rectangle area, bool dark)
    {
        var rows = new List<List<(int Left, int Width)>>(area.Height);
        for (int y = area.Top; y < area.Bottom; y++)
        {
            var runs = new List<(int Left, int Width)>();
            int left = area.Left;
            for (int x = left; x < area.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                bool foreground = dark ? color.R + color.G + color.B < 450
                    : color.R >= 150 && color.G >= 150 && color.B >= 150;
                if (foreground) continue;
                if (x - left >= 3) runs.Add((left, x - left));
                left = x + 1;
            }
            if (area.Right - left >= 3) runs.Add((left, area.Right - left));
            rows.Add(runs);
        }

        int first = 0, height = 0, runStart = 0;
        for (int y = 0; y < rows.Count; y++)
        {
            if (rows[y].Count == 0) runStart = y + 1;
            int length = y - runStart + 1;
            if (length <= height) continue;
            first = runStart;
            height = length;
        }
        if (height < (dark ? 10 : 16) || height > 32) return null;
        var top = rows[first + height / 5];
        var middle = rows[first + height / 2];
        var bottom = rows[first + height - 1 - height / 5];
        if (ReadConvergingStrokes(top, middle, rows[first + height * 2 / 3]) is { } roman)
            return roman;
        if (top.Count is < 1 or > 3 || middle.Count != top.Count || bottom.Count != top.Count)
            return null;

        for (int i = 0; i < top.Count; i++)
        {
            int width = middle[i].Width;
            if (width is < 4 or > 18 || Math.Abs(top[i].Width - width) > 3 ||
                Math.Abs(bottom[i].Width - width) > 3 ||
                Math.Abs(top[i].Left - bottom[i].Left) > 6 ||
                Math.Abs(2 * middle[i].Left - top[i].Left - bottom[i].Left) > 4)
                return null;
        }
        return top.Count;
    }

    private static int? ReadConvergingStrokes(List<(int Left, int Width)> top,
        List<(int Left, int Width)> middle, List<(int Left, int Width)> bottom)
    {
        if (top.Count is < 2 or > 3 || middle.Count != top.Count ||
            bottom.Count < top.Count - 1 || bottom.Count > top.Count)
            return null;

        for (int i = 0; i < top.Count - 1; i++)
        {
            int upperGap = Gap(top, i);
            if (upperGap < 5 || Gap(middle, i) > upperGap - 2) continue;

            if (bottom.Count == top.Count)
            {
                int lowerGap = Gap(bottom, i);
                if (lowerGap > upperGap / 2 || upperGap - lowerGap < 5) continue;
            }
            else
            {
                var merged = bottom[i];
                int right = top[i + 1].Left + top[i + 1].Width;
                if (merged.Left < top[i].Left - 3 || merged.Left + merged.Width > right + 3 ||
                    merged.Width < Math.Max(middle[i].Width, middle[i + 1].Width))
                    continue;
            }

            // The converging pair is V. A separate stroke before or after it distinguishes IV from VI.
            if (top.Count == 2) return 5;
            return i == 0 ? 6 : 4;
        }
        return null;

        static int Gap(List<(int Left, int Width)> row, int left) =>
            row[left + 1].Left - row[left].Left - row[left].Width;
    }

    private static Rectangle FindViewport(Bitmap bitmap, Rectangle bounds)
    {
        var fitted = GameViewport.Fit(new RECT { Right = bounds.Width, Bottom = bounds.Height },
            ReferenceWidth, ReferenceHeight);
        // Preserve wider render surfaces unless both margins actually contain black bars.
        if (fitted.Top >= 4 &&
            IsDarkLine(bitmap, fitted.Top - 2, horizontal: true) &&
            IsDarkLine(bitmap, fitted.Bottom + 1, horizontal: true))
            return Rectangle.FromLTRB(0, fitted.Top, bounds.Width, fitted.Bottom);
        if (fitted.Left >= 4 &&
            IsDarkLine(bitmap, fitted.Left - 2, horizontal: false) &&
            IsDarkLine(bitmap, fitted.Right + 1, horizontal: false))
            return Rectangle.FromLTRB(fitted.Left, 0, fitted.Right, bounds.Height);
        return bounds;
    }

    private static bool IsDarkLine(Bitmap bitmap, int index, bool horizontal)
    {
        int length = horizontal ? bitmap.Width : bitmap.Height;
        for (int i = 1; i < 16; i++)
        {
            int position = length * i / 16;
            var color = horizontal ? bitmap.GetPixel(position, index) : bitmap.GetPixel(index, position);
            if (color.R > 12 || color.G > 12 || color.B > 12) return false;
        }
        return true;
    }

    private static Bitmap Resize(Bitmap source, Rectangle viewport, Rectangle bounds)
    {
        if (viewport.Size == bounds.Size)
            return source.Clone(viewport, PixelFormat.Format24bppRgb);
        var resized = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, bounds, viewport, GraphicsUnit.Pixel);
        return resized;
    }

    private static byte[] Encode(Bitmap bitmap)
    {
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
