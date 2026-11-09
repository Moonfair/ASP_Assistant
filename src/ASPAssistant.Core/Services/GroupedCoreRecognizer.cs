using System.Drawing;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

internal static class GroupedCoreRecognizer
{
    internal sealed record Selection(int Side, IReadOnlyList<string>? Enabled);

    internal static Selection? Read(Bitmap image, IReadOnlyList<OcrTextResult> texts, IReadOnlyList<string> names)
    {
        var additionalTitle = texts.FirstOrDefault(t => t.Text.Contains("附加盟"));
        if (additionalTitle == null || names.Count == 0 || names.Count % 2 != 0) return null;
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

    private static bool HasSelectionBar(Bitmap image, int headingTop, double left, double right)
    {
        int x0 = (int)(image.Width * left), x1 = (int)(image.Width * right);
        int y0 = Math.Max((int)(image.Height * .15), headingTop - (int)(image.Height * .055));
        int y1 = Math.Min(image.Height, headingTop - (int)(image.Height * .018));
        int matchingRows = 0;
        for (int y = y0; y < y1; y++)
        {
            int green = 0, samples = 0;
            for (int x = x0; x < x1; x += 3)
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
