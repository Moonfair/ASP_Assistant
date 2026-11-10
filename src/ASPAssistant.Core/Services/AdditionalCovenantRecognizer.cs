using System.Drawing;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public static class AdditionalCovenantRecognizer
{
    public static IReadOnlyList<string>? FindDisabled(Bitmap image, IReadOnlyList<OcrTextResult> texts,
        IEnumerable<string> additionalNames)
    {
        var names = additionalNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderByDescending(n => n.Length).ToArray();
        if (names.Length == 0) return [];
        if (names.Length > 20) return null;
        var title = texts.FirstOrDefault(t => t.Text.Contains("附加盟"));
        if (title == null) return null;
        double w = image.Width, h = image.Height;
        var slots = new Dictionary<int, string>();
        foreach (var text in texts)
        {
            double y = text.Box.Y + text.Box.H / 2.0;
            int row = (int)Math.Round(((y - title.Box.Y) / h - .219) / .20);
            if (row < 0 || row >= (names.Length + 9) / 10 || Math.Abs(y - (title.Box.Y + h * (.219 + row * .20))) > h * .035) continue;
            var compact = Regex.Replace(text.Text, @"\s+", "");
            var matches = CovenantLabelMatcher.Find(compact, names).ToArray();
            if (!CovenantLabelMatcher.HasCompleteSlotCoverage(text, w, compact, matches)) return null;
            foreach (var match in matches)
            {
                double x = text.Box.X + text.Box.W * (match.Index + match.Length / 2.0) / compact.Length;
                int column = (int)Math.Round((x / w - .046875) / .0671875);
                int slot = row * 10 + column;
                if (column < 0 || column >= 10 || slot >= names.Length
                    || Math.Abs(x / w - (.046875 + column * .0671875)) > .025) return null;
                if (!slots.TryAdd(slot, match.Name)) return null;
            }
        }
        if (slots.Values.Distinct().Count() != slots.Count) return null;
        var disabled = new List<string>();
        for (int slot = 0; slot < names.Length; slot++)
        {
            double cx = w * (.046875 + slot % 10 * .0671875);
            double cy = title.Box.Y + h * (.219 + slot / 10 * .20 - .075);
            var state = CovenantIconClassifier.IsDisabled(image, cx, cy);
            if (state == null) return null;
            // Names can be occluded on green icons without affecting the disabled set.
            // Every physical slot must be classified; every gray slot needs a unique name.
            if (state.Value)
            {
                if (!slots.TryGetValue(slot, out var name)) return null;
                disabled.Add(name);
            }
        }
        return disabled;
    }
}
