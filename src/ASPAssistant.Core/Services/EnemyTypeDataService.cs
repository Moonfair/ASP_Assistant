using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Loads special-enemy type data from the pre-generated local
/// <c>data/enemy_types.json</c> (produced by <c>fetch_spdatabase.py</c>).
///
/// After <see cref="LoadAsync"/> completes, use <see cref="LookupByTypeName"/> to
/// resolve an OCR-detected suffix like "飞行" or "频次" to its <see cref="EnemyTypeEntry"/>.
/// </summary>
public sealed class EnemyTypeDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Indexed by Chinese type name (e.g. "飞行"), matching the OCR suffix.
    private Dictionary<string, EnemyTypeEntry>? _byTypeName;

    /// <summary>
    /// Loads <c>enemy_types.json</c> from the given data directory.
    /// Safe to call from a background thread.
    /// </summary>
    public async Task LoadAsync(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "enemy_types.json");
        if (!File.Exists(path))
        {
            AppLogger.Warn("EnemyTypeData", $"enemy_types.json not found at {path}");
            _byTypeName = [];
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var file = JsonSerializer.Deserialize<EnemyTypeFile>(json, JsonOptions);

            _byTypeName = file?.Types?.Values
                .Where(e => !string.IsNullOrEmpty(e.TypeName))
                .ToDictionary(e => e.TypeName)
                ?? [];

            AppLogger.Info("EnemyTypeData",
                $"Loaded {_byTypeName.Count} enemy type entries from {path}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("EnemyTypeData", $"Failed to load enemy_types.json: {ex.Message}");
            _byTypeName = [];
        }
    }

    /// <summary>
    /// Looks up an entry by the Chinese type-name suffix extracted from the OCR result
    /// (e.g. pass "飞行" for "特训敌人·飞行").
    /// Returns <c>null</c> if not found or not yet loaded.
    /// </summary>
    public EnemyTypeEntry? LookupByTypeName(string typeName)
    {
        if (_byTypeName is null) return null;

        // 1) Exact match first.
        if (_byTypeName.TryGetValue(typeName, out var entry))
            return entry;

        // 2) Normalized exact match (strip punctuation/noise).
        var normalized = NormalizeTypeName(typeName);
        if (normalized.Length == 0)
            return null;
        if (_byTypeName.TryGetValue(normalized, out entry))
            return entry;

        // 3) Near match with <=1 different character.
        string? bestName = null;
        int bestDiff = int.MaxValue;
        foreach (var key in _byTypeName.Keys)
        {
            var nk = NormalizeTypeName(key);
            int diff = CharDiffDistance(normalized, nk);
            if (diff < 0 || diff > 1)
                continue;
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestName = key;
                if (bestDiff == 0) break;
            }
        }

        if (bestName != null && _byTypeName.TryGetValue(bestName, out entry))
            return entry;

        return null;
    }

    private static string NormalizeTypeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var trimmed = raw.Trim().Trim('·', '：', ':', '，', ',', '。', '、', '?', '？', '!', '！', '“', '”', '"', '\'', ' ');
        var chars = trimmed.Where(c => c >= '\u4e00' && c <= '\u9fff').ToArray();
        return chars.Length > 0 ? new string(chars) : trimmed;
    }

    // Returns -1 when lengths differ too much; otherwise differing char count.
    private static int CharDiffDistance(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
            return -1;

        if (a.Length == b.Length)
        {
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    diff++;
            }
            return diff;
        }

        // One insertion/deletion tolerance.
        if (Math.Abs(a.Length - b.Length) > 1)
            return -1;

        var longer = a.Length >= b.Length ? a : b;
        var shorter = a.Length >= b.Length ? b : a;
        int iLong = 0, iShort = 0, diffCount = 0;
        while (iLong < longer.Length && iShort < shorter.Length)
        {
            if (longer[iLong] == shorter[iShort])
            {
                iLong++;
                iShort++;
            }
            else
            {
                diffCount++;
                iLong++;
                if (diffCount > 1)
                    return diffCount;
            }
        }

        if (iLong < longer.Length)
            diffCount++;
        return diffCount;
    }

    // ── JSON wrapper ──────────────────────────────────────────────────────────

    private sealed class EnemyTypeFile
    {
        public Dictionary<string, EnemyTypeEntry>? Types { get; set; }
    }
}
