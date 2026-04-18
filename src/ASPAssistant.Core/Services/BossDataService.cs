using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Loads Garrison Protocol boss data from the pre-generated local
/// <c>data/bosses.json</c> (produced by <c>fetch_spdatabase.py</c>).
///
/// Call <see cref="LoadAsync"/> once on startup; after that use
/// <see cref="Lookup"/> to resolve an OCR-detected boss name to its
/// <see cref="BossInfo"/>.
/// </summary>
public sealed class BossDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private Dictionary<string, BossInfo>? _byName;

    private List<(string Key, string CanonicalName)> _bossOcrMatchPairs = [];

    /// <summary>Stable list for multi-fragment OCR fallback (same order as JSON).</summary>
    private List<BossInfo> _bossesForFragmentMatch = [];

    /// <summary>
    /// Loads <c>bosses.json</c> from the given data directory.
    /// Safe to call from a background thread; the result is written atomically.
    /// </summary>
    public async Task LoadAsync(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "bosses.json");
        if (!File.Exists(path))
        {
            AppLogger.Warn("BossData", $"bosses.json not found at {path}");
            _byName = [];
            _bossOcrMatchPairs = [];
            _bossesForFragmentMatch = [];
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var file = JsonSerializer.Deserialize<BossDataFile>(json, JsonOptions);

            var bosses = file?.Bosses?
                .Where(b => !string.IsNullOrEmpty(b.Name))
                .OrderBy(b => b.EnemyId, StringComparer.Ordinal)
                .ToList() ?? [];

            _byName = [];
            foreach (var b in bosses)
            {
                if (!_byName.ContainsKey(b.Name))
                    _byName[b.Name] = b;
                foreach (var alias in b.Aliases ?? [])
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;
                    var key = alias.Trim();
                    if (_byName.ContainsKey(key))
                        continue;
                    _byName[key] = b;
                }
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            _bossOcrMatchPairs = [];
            foreach (var b in bosses)
            {
                AddOcrMatchKey(b.Name, b.Name, seenKeys, _bossOcrMatchPairs);
                foreach (var alias in b.Aliases ?? [])
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;
                    AddOcrMatchKey(alias.Trim(), b.Name, seenKeys, _bossOcrMatchPairs);
                }
            }

            _bossOcrMatchPairs.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            _bossesForFragmentMatch = [.. bosses];

            AppLogger.Info("BossData",
                $"Loaded {_byName.Count} boss lookup keys, {_bossOcrMatchPairs.Count} OCR match keys from {path} ({bosses.Count} entries)");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BossData", $"Failed to load bosses.json: {ex.Message}");
            _byName = [];
            _bossOcrMatchPairs = [];
            _bossesForFragmentMatch = [];
        }
    }

    private static void AddOcrMatchKey(
        string key,
        string canonicalName,
        HashSet<string> seenKeys,
        List<(string Key, string CanonicalName)> target)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 2)
            return;
        if (!seenKeys.Add(key))
            return;
        target.Add((key, canonicalName));
    }

    /// <summary>
    /// Finds the canonical boss display name: (1) substring on raw/normalized text;
    /// (2) multi-fragment match when OCR splits a name across lines (quotes/punctuation normalized).
    /// </summary>
    public bool TryMatchBossFromOcr(IReadOnlyList<string> ocrLines, [NotNullWhen(true)] out string? canonicalName)
    {
        canonicalName = null;
        if (_bossOcrMatchPairs.Count == 0 && _bossesForFragmentMatch.Count == 0)
            return false;

        var merged = string.Concat(ocrLines);
        if (TryMatchHaystack(merged, out canonicalName))
            return true;

        var mergedNorm = NormalizeOcrForMatch(merged);
        if (mergedNorm.Length > 0 && TryMatchHaystackNormalized(mergedNorm, out canonicalName))
            return true;

        foreach (var line in ocrLines)
        {
            if (TryMatchHaystack(line, out canonicalName))
                return true;
            var ln = NormalizeOcrForMatch(line);
            if (ln.Length > 0 && TryMatchHaystackNormalized(ln, out canonicalName))
                return true;
        }

        if (TryMatchByNameFragments(mergedNorm, merged, out canonicalName))
            return true;

        return false;
    }

    private bool TryMatchHaystack(string haystack, [NotNullWhen(true)] out string? canonicalName)
    {
        canonicalName = null;
        if (string.IsNullOrEmpty(haystack))
            return false;

        foreach (var (key, canonical) in _bossOcrMatchPairs)
        {
            if (haystack.Contains(key, StringComparison.Ordinal))
            {
                canonicalName = canonical;
                return true;
            }
        }

        return false;
    }

    /// <summary>Like <see cref="TryMatchHaystack"/> but keys are normalized the same way as OCR lines.</summary>
    private bool TryMatchHaystackNormalized(string hayNormalized, [NotNullWhen(true)] out string? canonicalName)
    {
        canonicalName = null;
        foreach (var (key, canonical) in _bossOcrMatchPairs)
        {
            var kn = NormalizeOcrForMatch(key);
            if (kn.Length < 2)
                continue;
            if (hayNormalized.Contains(kn, StringComparison.Ordinal))
            {
                canonicalName = canonical;
                return true;
            }
        }

        // OCR fallback: allow one-character difference in a contiguous segment.
        // This handles cases like "假想敌：统" vs canonical "假想敌：铳".
        foreach (var (key, canonical) in _bossOcrMatchPairs)
        {
            var kn = NormalizeOcrForMatch(key);
            if (kn.Length < 3)
                continue;
            if (ContainsApproximateSubstring(hayNormalized, kn, maxDiff: 1))
            {
                canonicalName = canonical;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsApproximateSubstring(string hay, string needle, int maxDiff)
    {
        if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle) || hay.Length < needle.Length)
            return false;

        int n = needle.Length;
        for (int i = 0; i <= hay.Length - n; i++)
        {
            int diff = 0;
            for (int j = 0; j < n; j++)
            {
                if (hay[i + j] == needle[j])
                    continue;
                diff++;
                if (diff > maxDiff)
                    break;
            }

            if (diff <= maxDiff)
                return true;
        }

        return false;
    }

    /// <summary>
    /// When the full boss name never appears contiguously, require every derived fragment
    /// (e.g. 卢西恩 + 猩红血钻) to appear in the combined OCR. More specific bosses first.
    /// </summary>
    private bool TryMatchByNameFragments(
        string hayNormalized,
        string hayOriginal,
        [NotNullWhen(true)] out string? canonicalName)
    {
        canonicalName = null;
        if (_bossesForFragmentMatch.Count == 0)
            return false;

        foreach (var boss in _bossesForFragmentMatch
                     .OrderByDescending(b => b.Name.Length)
                     .ThenByDescending(b => GetBossNameFragments(b).Count))
        {
            var frags = GetBossNameFragments(boss);
            if (frags.Count == 0)
                continue;

            if (frags.All(f =>
                    hayNormalized.Contains(f, StringComparison.Ordinal)
                    || hayOriginal.Contains(f, StringComparison.Ordinal)))
            {
                canonicalName = boss.Name;
                return true;
            }

            foreach (var alias in boss.Aliases ?? [])
            {
                if (string.IsNullOrWhiteSpace(alias))
                    continue;
                var a = alias.Trim();
                if (a.Length < 2)
                    continue;
                if (hayNormalized.Contains(a, StringComparison.Ordinal)
                    || hayOriginal.Contains(a, StringComparison.Ordinal))
                {
                    canonicalName = boss.Name;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Splits display name into non-trivial fragments (handles “ ” quotes).</summary>
    private static List<string> GetBossNameFragments(BossInfo boss)
    {
        var name = boss.Name;
        if (string.IsNullOrWhiteSpace(name))
            return [];

        var parts = name.Split(['，', ',', '：', ':', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var frags = new List<string>();
        foreach (var p in parts)
        {
            var t = p.Trim().Trim('"', '\u201c', '\u201d', '\u2018', '\u2019', '「', '」', '『', '』');
            if (t.Length >= 2)
                frags.Add(t);
            else if (t.Length == 1 && parts.Length >= 2)
                frags.Add(t);
        }

        if (frags.Count >= 2)
            return frags;

        var one = NormalizeOcrForMatch(name);
        if (one.Length >= 4)
            return [one];

        return [];
    }

    /// <summary>Strips OCR-noisy punctuation so split names can still match across segments.</summary>
    private static string NormalizeOcrForMatch(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is '"' or '\u201c' or '\u201d' or '\u2018' or '\u2019' or '\u00a0'
                or '「' or '」' or '『' or '』' or '\'' or ' ')
                continue;
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Looks up boss info by exact display name or alias.
    /// </summary>
    public BossInfo? Lookup(string bossName)
    {
        if (_byName is null) return null;
        _byName.TryGetValue(bossName, out var info);
        return info;
    }

    // ── JSON wrapper ──────────────────────────────────────────────────────────

    private sealed class BossDataFile
    {
        public List<BossInfo>? Bosses { get; set; }
    }
}
