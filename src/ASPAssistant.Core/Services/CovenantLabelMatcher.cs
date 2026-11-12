using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

/// <summary>Matches OCR labels against the current covenant catalog without guessing ambiguous names.</summary>
internal static class CovenantLabelMatcher
{
    internal readonly record struct LabelMatch(string Name, int Index, int Length);

    internal static bool HasCompleteSlotCoverage(OcrTextResult text, CovenantGrid grid,
        string compact, IReadOnlyList<LabelMatch> matches)
    {
        if (matches.Count == 0) return true;
        var covered = Enumerable.Range(0, 10).Where(column =>
        {
            double center = grid.ColumnCenter(column);
            return center >= text.Box.X && center <= text.Box.X + text.Box.W;
        }).ToArray();
        if (covered.Length < 2) return true;
        // A merged box must provide one name per physical slot it spans. Otherwise,
        // missing whole words would redistribute the remaining text onto other icons.
        var located = matches.Select(match => grid.NearestColumn(
            text.Box.X + text.Box.W * (match.Index + match.Length / 2.0) / compact.Length));
        return located.SequenceEqual(covered);
    }

    internal static IEnumerable<LabelMatch> Find(string compact, IReadOnlyList<string> names)
    {
        // Exact names anchor merged OCR boxes. Only a whole unmatched span between
        // anchors may be corrected; do not search arbitrary fuzzy substrings.
        var pattern = string.Join("|", names.OrderByDescending(n => n.Length).Select(Regex.Escape));
        int offset = 0;
        foreach (Match exact in Regex.Matches(compact, pattern))
        {
            var gap = MatchGap(compact, offset, exact.Index - offset, names);
            if (gap.HasValue) yield return gap.Value;
            yield return new(exact.Value, exact.Index, exact.Length);
            offset = exact.Index + exact.Length;
        }
        var tail = MatchGap(compact, offset, compact.Length - offset, names);
        if (tail.HasValue) yield return tail.Value;
    }

    private static LabelMatch? MatchGap(string compact, int start, int length, IReadOnlyList<string> names)
    {
        if (length < 2) return null;
        string observed = compact.Substring(start, length);
        string? candidate = null;
        foreach (var name in names)
        {
            // Short names such as 炎/精准 need exact recognition. For longer names,
            // allow one missing, extra or substituted character and require uniqueness
            // across the entire catalog (not only names remaining in other slots).
            if (name.Length < 3 || !OneEditApart(observed, name)) continue;
            if (candidate != null) return null;
            candidate = name;
        }
        // Keep the OCR span length for positioning, not the corrected name's length.
        return candidate == null ? null : new(candidate, start, length);
    }

    private static bool OneEditApart(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1) return false;
        int i = 0, j = 0, edits = 0;
        while (i < left.Length && j < right.Length)
        {
            if (left[i] == right[j])
            {
                i++;
                j++;
                continue;
            }
            if (++edits > 1) return false;
            if (left.Length >= right.Length) i++;
            if (right.Length >= left.Length) j++;
        }
        return edits + left.Length - i + right.Length - j == 1;
    }
}
