using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Recognizes the enemy encounter info on the ban screen ("确认本局信息").
///
/// Recognition flow:
/// 1. OCR the <c>EnemySectionScan</c> region to locate the anchor text "本局遭遇敌方".
/// 2. From the anchor's top-left, define a block of width 90% × height 35% of the image.
/// 3. OCR the block and extract:
///    - Boss name by substring match against all names/aliases in <see cref="BossDataService"/> (longest match first).
///    - Enemy types from segments matching "特训敌人" prefix → strips suffix, keeps 1-4 char name.
/// </summary>
public sealed class EnemyRecognizer
{
    private readonly IOcrEngine _ocr;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly BossDataService _bossData;

    /// <summary>
    /// Width of the recognition block as a fraction of screenshot width.
    /// Measured from the anchor's X position.
    /// </summary>
    public double BlockWidthPercent { get; init; } = 0.9;

    /// <summary>
    /// Height of the recognition block as a fraction of screenshot height.
    /// Measured from the anchor's Y position.
    /// </summary>
    public double BlockHeightPercent { get; init; } = 0.35;

    public EnemyRecognizer(IOcrEngine ocr, IOcrStrategy ocrStrategy, BossDataService bossData)
    {
        _ocr       = ocr;
        _ocrStrategy = ocrStrategy;
        _bossData  = bossData;
    }

    /// <summary>
    /// Runs enemy recognition on the given screenshot.
    /// Returns <c>null</c> if the anchor text is not found or no enemy data is detected.
    /// </summary>
    public async Task<EnemyResult?> RecognizeAsync(byte[] screenshot, int imgWidth, int imgHeight)
    {
        var regions = _ocrStrategy.GetScanRegions();
        var scanRegion = regions.FirstOrDefault(r => r.Name == "EnemySectionScan");
        if (scanRegion == null)
        {
            AppLogger.Warn("EnemyRecognizer", "EnemySectionScan region not found");
            return null;
        }

        // ── Step 1: find anchor "本局遭遇敌方" ────────────────────────────────
        int sx = (int)(scanRegion.XPercent      * imgWidth);
        int sy = (int)(scanRegion.YPercent      * imgHeight);
        int sw = (int)(scanRegion.WidthPercent  * imgWidth);
        int sh = (int)(scanRegion.HeightPercent * imgHeight);

        var scanOcr = await _ocr.RecognizeRegionAsync(screenshot, sx, sy, sw, sh);
        var anchor  = scanOcr.FirstOrDefault(r => r.Text.Contains("本局遭遇敌方"));

        if (anchor == null)
        {
            AppLogger.Warn("EnemyRecognizer",
                $"Anchor '本局遭遇敌方' not found " +
                $"({scanOcr.Count} results: [{string.Join(", ", scanOcr.Select(r => r.Text))}])");
            return null;
        }

        AppLogger.Info("EnemyRecognizer",
            $"Anchor found at ({anchor.Box.X}, {anchor.Box.Y})");

        // ── Step 2: define recognition block from anchor ───────────────────────
        int blockX = anchor.Box.X;
        int blockY = anchor.Box.Y;
        int blockW = Math.Min((int)(BlockWidthPercent  * imgWidth),  imgWidth  - blockX);
        int blockH = Math.Min((int)(BlockHeightPercent * imgHeight), imgHeight - blockY);

        if (blockW <= 0 || blockH <= 0)
        {
            AppLogger.Warn("EnemyRecognizer", "Recognition block has zero size");
            return null;
        }

        AppLogger.Info("EnemyRecognizer",
            $"Recognition block = ({blockX}, {blockY}, {blockW}, {blockH})");

        // ── Step 3: OCR the block and extract boss + enemy types ───────────────
        var blockOcr = await _ocr.RecognizeRegionAsync(screenshot, blockX, blockY, blockW, blockH);

        AppLogger.Info("EnemyRecognizer",
            $"Raw OCR ({blockOcr.Count}): [{string.Join(", ", blockOcr.Select(r => r.Text))}]");

        var ocrLines = blockOcr
            .Select(r => r.Text.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        string bossName = "";
        if (_bossData.TryMatchBossFromOcr(ocrLines, out var matchedBoss))
        {
            bossName = matchedBoss;
            AppLogger.Info("EnemyRecognizer", $"Boss: '{bossName}'");
        }

        var enemyTypes = new List<string>();
        foreach (var result in blockOcr)
        {
            var text = result.Text.Trim();
            foreach (var e in ExtractEnemyTypes(text))
            {
                enemyTypes.Add(e);
                AppLogger.Info("EnemyRecognizer", $"Enemy type: '{e}'");
            }
        }

        AppLogger.Info("EnemyRecognizer",
            $"Result — boss='{bossName}' enemyTypes=[{string.Join(", ", enemyTypes)}]");

        return new EnemyResult(bossName, enemyTypes);
    }

    /// <summary>
    /// Scans <paramref name="text"/> for every occurrence of "特训敌人·XXX" or "特训敌·XXX"
    /// (OCR sometimes drops the "人" character).  One segment may contain multiple merged
    /// entries such as "特训敌人·频次特训敌人·元素" or "特训敌·折射特训敌·元素".
    /// Returns full strings preserving the detected prefix, e.g.
    /// ["特训敌人·频次", "特训敌人·元素"] or ["特训敌·折射", "特训敌·元素"].
    /// </summary>
    private static List<string> ExtractEnemyTypes(string text)
    {
        // OCR may misread "敌" as visually similar chars (e.g. "致").
        // Accept both token variants, but normalize output prefix to "特训敌/特训敌人".
        var baseTokens = new[] { "特训敌", "特训致" };
        var results = new List<string>();

        int searchFrom = 0;
        while (searchFrom < text.Length)
        {
            int idx = FindNextTokenIndex(text, baseTokens, searchFrom, out var matchedToken);
            if (idx < 0) break;

            int pos = idx + matchedToken.Length;

            // Determine full prefix variant.
            bool hasRen = pos < text.Length && text[pos] == '人';
            string fullPfx = hasRen ? "特训敌人" : "特训敌";
            if (hasRen) pos++;

            // Skip one optional separator (·, ·, ：, :).
            int nameStart = pos;
            if (nameStart < text.Length && "··：:，,、。".Contains(text[nameStart]))
                nameStart++;

            // Name ends at the next "特训敌" occurrence or after 4 chars,
            // whichever comes first.
            int nextIdx = FindNextTokenIndex(text, baseTokens, nameStart, out _);
            int nameEnd = nextIdx >= 0
                ? Math.Min(nameStart + 4, nextIdx)
                : Math.Min(nameStart + 4, text.Length);

            if (nameEnd > nameStart)
            {
                var name = NormalizeEnemyTypeName(text[nameStart..nameEnd]);
                if (!string.IsNullOrEmpty(name))
                    results.Add($"{fullPfx}·{name}");
            }

            // Advance past the base token so the next iteration finds the subsequent entry.
            searchFrom = idx + matchedToken.Length;
        }

        return results;
    }

    private static int FindNextTokenIndex(string text, string[] tokens, int start, out string matchedToken)
    {
        int bestIdx = -1;
        matchedToken = "";
        foreach (var token in tokens)
        {
            int idx = text.IndexOf(token, start, StringComparison.Ordinal);
            if (idx < 0)
                continue;
            if (bestIdx < 0 || idx < bestIdx)
            {
                bestIdx = idx;
                matchedToken = token;
            }
        }
        return bestIdx;
    }

    private static string NormalizeEnemyTypeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var s = raw.Trim();

        // Remove common OCR punctuation noise at both ends.
        s = s.Trim('·', '：', ':', '，', ',', '。', '、', '?', '？', '!', '！', '“', '”', '"', '\'', ' ');

        // Keep only likely type-name characters (primarily CJK); this drops
        // stray OCR symbols like "?" that often appear around the token.
        var filtered = new string(s.Where(c => c >= '\u4e00' && c <= '\u9fff').ToArray());
        if (!string.IsNullOrEmpty(filtered))
            s = filtered;

        return s.Trim();
    }
}
