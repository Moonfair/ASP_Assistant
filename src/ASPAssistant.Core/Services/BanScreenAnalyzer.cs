using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Represents the ban state of a single covenant.
/// </summary>
public record CovenantBanState(string Name, bool IsBanned, double X);

/// <summary>
/// Result produced by one full ban-screen analysis pass.
/// </summary>
public record BanScreenResult(
    IReadOnlyList<CovenantBanState> CoreCovenants,
    IReadOnlyList<CovenantBanState> AdditionalCovenants,
    string? EnemyFaction);

/// <summary>
/// Orchestrates the automated ban-screen analysis workflow based on
/// three screenshots + two anchor-based scrolls.
/// </summary>
public sealed class BanScreenAnalyzer
{
    private const int SectionTopPaddingPx = 200;
    private const int AnchorRetryNudgeClicks = -1;

    private readonly ScreenCaptureService _capture;
    private readonly IOcrEngine _ocr;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly EnemyRecognizer _enemyRecognizer;
    private readonly MaaScrollService _maaScroll;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _groupCovenantNames;
    private readonly IReadOnlyList<Operator> _operators;

    // ── Scroll parameters ─────────────────────────────────────────────────────

    /// <summary>
    /// Delay in milliseconds after any scroll command before taking the next
    /// screenshot, to allow the game's scroll animation to complete.
    /// </summary>
    public int ScrollSettleMs { get; init; } = 900;

    /// <summary>
    /// Number of WM_MOUSEWHEEL click-units (×120) for the main half-screen scroll
    /// between the core and additional covenant sections. Negative = down.
    /// </summary>
    public int ScrollClicks { get; init; } = -10;

    /// <summary>
    /// Assumed fraction of screen height scrolled by <see cref="ScrollClicks"/> clicks.
    /// Used to convert an overflow pixel distance into extra scroll clicks.
    /// </summary>
    public double ScrollHalfScreenFraction { get; init; } = 0.5;

    // ── Ban counts ────────────────────────────────────────────────────────────

    /// <summary>Number of rightmost core covenants that are banned.</summary>
    public int CoreBanCount { get; init; } = 3;

    /// <summary>Number of rightmost additional covenants that are banned.</summary>
    public int AdditionalBanCount { get; init; } = 4;

    // ── Covenant block size (relative to image dimensions) ────────────────────

    /// <summary>
    /// Width of the covenant block region as a fraction of screenshot width.
    /// </summary>
    public double CovenantRegionWidthPercent { get; init; } = 1.00;

    /// <summary>
    /// Height of the core covenant block region as a fraction of screenshot height.
    /// The additional covenant block is
    /// <see cref="AdditionalRegionHeightMultiplier"/> times this value.
    /// </summary>
    public double CovenantRegionHeightPercent { get; init; } = 0.28;

    /// <summary>
    /// Multiplier applied to <see cref="CovenantRegionHeightPercent"/> for the
    /// additional covenant section.  Default 2.0 (twice the core section height).
    /// </summary>
    public double AdditionalRegionHeightMultiplier { get; init; } = 2.0;

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when a full analysis pass completes successfully.
    /// </summary>
    public event Action<BanScreenResult>? AnalysisCompleted;

    /// <summary>
    /// Raised when enemy recognition completes (before the full analysis finishes).
    /// Fired even if recognition returns partial results.
    /// </summary>
    public event Action<EnemyResult>? EnemyDetected;

    /// <summary>
    /// Raised when ban-screen analysis starts/ends. true = busy, false = idle.
    /// </summary>
    public event Action<bool>? AnalysisBusyChanged;

    /// <summary>
    /// Raised when auto-ban calculation for the current run completes.
    /// </summary>
    public event Action<IReadOnlyList<string>>? AutoBanListComputed;

    /// <summary>
    /// Raised when a full-screen game-window announcement should be shown/hidden.
    /// </summary>
    public event Action<string>? ShowGameOverlayMessageRequested;
    public event Action? HideGameOverlayMessageRequested;

    // Re-entry guard: only one analysis can run at a time.
    private int _running;

    public BanScreenAnalyzer(
        ScreenCaptureService capture,
        IOcrEngine ocr,
        IOcrStrategy ocrStrategy,
        EnemyRecognizer enemyRecognizer,
        MaaScrollService maaScroll,
        IReadOnlyList<Operator> operators,
        IReadOnlyList<string> coreCovenantNames,
        IReadOnlyList<string> additionalCovenantNames)
    {
        _capture = capture;
        _ocr = ocr;
        _ocrStrategy = ocrStrategy;
        _enemyRecognizer = enemyRecognizer;
        _maaScroll = maaScroll;
        _operators = operators;
        _groupCovenantNames = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Core"]       = coreCovenantNames,
            ["Additional"] = additionalCovenantNames,
        };
    }

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full ban-screen analysis asynchronously.
    /// Silently returns if another analysis is already in progress.
    /// </summary>
    public async Task RunAnalysisAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            AppLogger.Warn("BanAnalyzer", "Analysis already running, skipping");
            return;
        }

        AnalysisBusyChanged?.Invoke(true);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AppLogger.Info("BanAnalyzer", "Starting ban-screen analysis");
            await ShowTimedGameOverlayMessageAsync("ban位识别即将开始，请勿操作鼠标", 2000);

            // ── Screenshot #1: enemy parse + locate Core anchor ─────────────────
            AppConsole.WriteLine("[BanAnalyzer] 截图 #1...");
            var shot1 = _capture.CaptureScreen();
            if (shot1 == null)
            {
                AppLogger.Warn("BanAnalyzer", "Screenshot #1 failed");
                AppConsole.WriteLine("[BanAnalyzer] 截图 #1 失败，终止");
                return;
            }
            var (w1, h1) = GetPngDimensions(shot1);
            AppConsole.WriteLine($"[BanAnalyzer] 截图 #1 完成 ({w1}×{h1})");

            var enemyTask = RecognizeEnemyAsync(shot1, w1, h1);
            var coreAnchorResult = await FindAnchorWithDownwardRetryAsync("Core", shot1, w1, h1);
            shot1 = coreAnchorResult.Screenshot;
            w1 = coreAnchorResult.ImgWidth;
            h1 = coreAnchorResult.ImgHeight;
            var coreAnchor = coreAnchorResult.Anchor;
            await ScrollSectionTopToScreenTopAsync("Core", coreAnchor, h1);

            // ── Screenshot #2: core ban parse + locate Additional anchor ───────
            AppConsole.WriteLine("[BanAnalyzer] 截图 #2...");
            var shot2 = _capture.CaptureScreen();
            if (shot2 == null)
            {
                AppLogger.Warn("BanAnalyzer", "Screenshot #2 failed, fallback to screenshot #1");
                AppConsole.WriteLine("[BanAnalyzer] 截图 #2 失败，使用截图 #1 代替");
                shot2 = shot1;
            }
            var (w2, h2) = GetPngDimensions(shot2);
            AppConsole.WriteLine($"[BanAnalyzer] 截图 #2 完成 ({w2}×{h2})");

            var additionalAnchorResult = await FindAnchorWithDownwardRetryAsync("Additional", shot2, w2, h2);
            shot2 = additionalAnchorResult.Screenshot;
            w2 = additionalAnchorResult.ImgWidth;
            h2 = additionalAnchorResult.ImgHeight;
            var additionalAnchor = additionalAnchorResult.Anchor;
            await ScrollSectionTopToScreenTopAsync("Additional", additionalAnchor, h2);

            // ── Screenshot #3: additional ban parse ─────────────────────────────
            AppConsole.WriteLine("[BanAnalyzer] 截图 #3...");
            var shot3 = _capture.CaptureScreen();
            if (shot3 == null)
            {
                AppLogger.Warn("BanAnalyzer", "Screenshot #3 failed, fallback to screenshot #2");
                AppConsole.WriteLine("[BanAnalyzer] 截图 #3 失败，使用截图 #2 代替");
                shot3 = shot2;
            }
            var (w3, h3) = GetPngDimensions(shot3);
            AppConsole.WriteLine($"[BanAnalyzer] 截图 #3 完成 ({w3}×{h3})");

            // Start covenant parsing only after all scroll operations have finished.
            var coreTask = RecognizeCovenantsAsync(shot2, w2, h2, "Core");
            var additionalTask = RecognizeCovenantsAsync(shot3, w3, h3, "Additional");
            await Task.WhenAll(enemyTask, coreTask, additionalTask);

            var enemyFaction = await enemyTask;
            var coreCovenants = await coreTask;
            var additionalCovenants = await additionalTask;

            sw.Stop();
            var result = new BanScreenResult(coreCovenants, additionalCovenants, enemyFaction);
            var autoBans = ComputeAutoBans(result);
            LogRecognitionSummary(result, autoBans);
            AutoBanListComputed?.Invoke(autoBans);
            LogResults(result, sw.ElapsedMilliseconds);
            AnalysisCompleted?.Invoke(result);

            await ShowTimedGameOverlayMessageAsync("识别成功！ASP Assistant 祝您卫戍愉快！", 2000);
        }
        catch (Exception ex)
        {
            AppLogger.Error("BanAnalyzer", "Analysis failed", ex);
        }
        finally
        {
            AnalysisBusyChanged?.Invoke(false);
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task ShowTimedGameOverlayMessageAsync(string message, int durationMs)
    {
        ShowGameOverlayMessageRequested?.Invoke(message);
        await Task.Delay(durationMs);
        HideGameOverlayMessageRequested?.Invoke();
    }

    private async Task<(System.Drawing.Point? Anchor, byte[] Screenshot, int ImgWidth, int ImgHeight)>
        FindAnchorWithDownwardRetryAsync(string group, byte[] screenshot, int imgWidth, int imgHeight)
    {
        var anchor = await FindSectionAnchorByGroupAsync(screenshot, imgWidth, imgHeight, group);
        if (anchor != null)
            return (anchor, screenshot, imgWidth, imgHeight);

        var hwnd = User32.FindArknightsWindow();
        if (hwnd == IntPtr.Zero)
        {
            AppLogger.Warn("BanAnalyzer",
                $"{group}: anchor not found and game window unavailable, skip retry nudge");
            return (null, screenshot, imgWidth, imgHeight);
        }

        int dy = AnchorRetryNudgeClicks * 120;
        AppConsole.WriteLine($"[BanAnalyzer] 未识别到 {group}，先下滑一次重试 (dy={dy})...");
        bool ok = _maaScroll.TryScrollAtWindowCenter(hwnd, dy);
        AppLogger.Info("BanAnalyzer", $"{group}: retry nudge scroll dy={dy} success={ok}");
        await Task.Delay(ScrollSettleMs);

        var retryShot = _capture.CaptureScreen();
        if (retryShot == null)
        {
            AppLogger.Warn("BanAnalyzer", $"{group}: retry screenshot failed after nudge");
            return (null, screenshot, imgWidth, imgHeight);
        }

        var (retryW, retryH) = GetPngDimensions(retryShot);
        var retryAnchor = await FindSectionAnchorByGroupAsync(retryShot, retryW, retryH, group);
        return (retryAnchor, retryShot, retryW, retryH);
    }

    // ── Enemy recognition ─────────────────────────────────────────────────────

    private async Task<string?> RecognizeEnemyAsync(byte[] screenshot, int w, int h)
    {
        var result = await _enemyRecognizer.RecognizeAsync(screenshot, w, h);
        if (result == null)
        {
            AppLogger.Warn("BanAnalyzer", "Enemy recognition returned no result");
            AppConsole.WriteLine("[BanAnalyzer] 识别本场敌人：未识别到敌人信息");
            return null;
        }

        EnemyDetected?.Invoke(result);
        AppConsole.WriteLine($"[BanAnalyzer] 识别本场敌人完成: 首领={result.BossName} 类型=[{string.Join(", ", result.EnemyTypes)}]");
        return result.BossName;
    }

    // ── Covenant recognition ──────────────────────────────────────────────────

    private static readonly Dictionary<string, string> GroupHeaderText = new()
    {
        ["Core"]       = "核心盟约",
        ["Additional"] = "附加盟约",
    };

    private async Task<IReadOnlyList<CovenantBanState>> RecognizeCovenantsAsync(
        byte[] screenshot, int imgWidth, int imgHeight, string group)
    {
        var anchor = await FindSectionAnchorByGroupAsync(screenshot, imgWidth, imgHeight, group);
        if (anchor == null)
            return [];

        int anchorX = anchor.Value.X;
        int anchorY = anchor.Value.Y;

        double heightPercent = group == "Additional"
            ? CovenantRegionHeightPercent * AdditionalRegionHeightMultiplier
            : CovenantRegionHeightPercent;

        // ── Step 2: define the covenant block ─────────────────────────────────
        int blockW = (int)(CovenantRegionWidthPercent * imgWidth);
        int blockH = (int)(heightPercent * imgHeight);

        blockW = Math.Min(blockW, imgWidth  - anchorX);
        blockH = Math.Min(blockH, imgHeight - anchorY);

        if (blockW <= 0 || blockH <= 0)
        {
            AppLogger.Warn("BanAnalyzer", $"{group}: covenant block has zero size after clamping");
            return [];
        }

        AppLogger.Info("BanAnalyzer",
            $"{group}: covenant block = ({anchorX}, {anchorY}, {blockW}, {blockH})");

        // ── Step 3: OCR the block; match to known covenant names ──────────────
        var blockOcr = await _ocr.RecognizeRegionAsync(screenshot, anchorX, anchorY, blockW, blockH);

        AppLogger.Info("BanAnalyzer",
            $"{group} raw OCR ({blockOcr.Count}): " +
            $"[{string.Join(", ", blockOcr.Select(r => r.Text))}]");

        _groupCovenantNames.TryGetValue(group, out var knownNames);
        knownNames ??= [];

        var seen        = new HashSet<string>();
        var nameResults = new List<(OcrTextResult Ocr, string CovenantName)>();

        foreach (var r in blockOcr)
        {
            var matched = MatchToCovenant(r.Text, knownNames);
            if (matched == null || !seen.Add(matched)) continue;
            nameResults.Add((r, matched));
        }

        AppLogger.Info("BanAnalyzer",
            $"{group} matched names ({nameResults.Count}): " +
            $"[{string.Join(", ", nameResults.Select(x => $"{x.Ocr.Text}→{x.CovenantName}"))}]");

        if (nameResults.Count == 0)
            return [];

        // ── Step 4: assign ban state by position (rightmost N = banned) ────────
        int banCount = group == "Additional" ? AdditionalBanCount : CoreBanCount;

        // Sort left→right; the last `banCount` entries are the banned ones.
        var sorted = nameResults
            .OrderBy(x => x.Ocr.Box.X + x.Ocr.Box.W / 2.0)
            .ToList();

        var bannedNames = group == "Additional"
            ? ComputeAdditionalSecondRowBans(sorted, banCount)
            : sorted
                .Skip(Math.Max(0, sorted.Count - banCount))
                .Select(x => x.CovenantName)
                .ToHashSet();

        var states = sorted
            .Select((x, i) =>
            {
                double cx = x.Ocr.Box.X + x.Ocr.Box.W / 2.0;
                bool banned = bannedNames.Contains(x.CovenantName);
                AppLogger.Info("BanAnalyzer",
                    $"{group} '{x.CovenantName}' (ocr='{x.Ocr.Text}') x≈{cx:F0} banned={banned}");
                return new CovenantBanState(x.CovenantName, banned, cx);
            })
            .ToList();

        return states;
    }

    private IReadOnlyList<string> ComputeAutoBans(BanScreenResult result)
    {
        var bannedCoreCovenants = result.CoreCovenants
            .Where(c => c.IsBanned)
            .Select(c => c.Name)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.Ordinal);
        var bannedAdditionalCovenants = result.AdditionalCovenants
            .Where(c => c.IsBanned)
            .Select(c => c.Name)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.Ordinal);
        var bannedCovenants = bannedCoreCovenants
            .Concat(bannedAdditionalCovenants)
            .ToHashSet(StringComparer.Ordinal);

        var autoBannedOperators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in _operators)
        {
            if (string.IsNullOrWhiteSpace(op.Name))
                continue;

            var allCovenants = op.CoreCovenants
                .Concat(op.AdditionalCovenants)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Rule 1: ban operators that have exactly one covenant and that covenant
            // is one of the banned covenants in this run.
            bool rule1 = allCovenants.Count == 1 && bannedCovenants.Contains(allCovenants[0]);

            // Rule 2: ban operators that have at least one banned core covenant and
            // at least one banned additional covenant.
            bool hasBannedCore = op.CoreCovenants.Any(c => bannedCoreCovenants.Contains(c));
            bool hasBannedAdditional = op.AdditionalCovenants.Any(c => bannedAdditionalCovenants.Contains(c));
            bool rule2 = hasBannedCore && hasBannedAdditional;

            if (rule1 || rule2)
                autoBannedOperators.Add(op.Name);
        }

        AppLogger.Info("BanAnalyzer",
            $"Auto-ban computed: total={autoBannedOperators.Count} " +
            $"ruleContext(coreBanned={bannedCoreCovenants.Count}, additionalBanned={bannedAdditionalCovenants.Count}, banned={bannedCovenants.Count}) " +
            $"operators=[{string.Join(", ", autoBannedOperators)}]");

        return autoBannedOperators.ToList();
    }

    private static void LogRecognitionSummary(BanScreenResult result, IReadOnlyList<string> autoBans)
    {
        string enemy = string.IsNullOrWhiteSpace(result.EnemyFaction) ? "(未识别)" : result.EnemyFaction;
        string core = string.Join(", ", result.CoreCovenants
            .Select(c => $"{c.Name}:{(c.IsBanned ? "禁用" : "进入")}"));
        string additional = string.Join(", ", result.AdditionalCovenants
            .Select(c => $"{c.Name}:{(c.IsBanned ? "禁用" : "进入")}"));
        string bans = string.Join(", ", autoBans);

        AppLogger.Info("BanAnalyzer",
            $"识别结果汇总: 敌人={enemy}; 核心盟约=[{core}]; 附加盟约=[{additional}]; 自动禁用干员=[{bans}]");
    }

    private static HashSet<string> ComputeAdditionalSecondRowBans(
        List<(OcrTextResult Ocr, string CovenantName)> entries,
        int banCount)
    {
        if (entries.Count == 0 || banCount <= 0)
            return [];

        var withCenterY = entries
            .Select(e => new
            {
                Entry = e,
                CenterY = e.Ocr.Box.Y + e.Ocr.Box.H / 2.0
            })
            .OrderBy(e => e.CenterY)
            .ToList();

        // Row clustering by vertical center to separate two visual lines.
        double avgHeight = entries.Average(e => Math.Max(1, e.Ocr.Box.H));
        double rowGapThreshold = Math.Max(12.0, avgHeight * 0.6);
        var rows = new List<List<(OcrTextResult Ocr, string CovenantName)>>();
        var current = new List<(OcrTextResult Ocr, string CovenantName)> { withCenterY[0].Entry };
        double currentMeanY = withCenterY[0].CenterY;

        foreach (var item in withCenterY.Skip(1))
        {
            if (Math.Abs(item.CenterY - currentMeanY) <= rowGapThreshold)
            {
                current.Add(item.Entry);
                currentMeanY = current.Average(x => x.Ocr.Box.Y + x.Ocr.Box.H / 2.0);
            }
            else
            {
                rows.Add(current);
                current = [item.Entry];
                currentMeanY = item.CenterY;
            }
        }
        rows.Add(current);

        // If row split failed, fall back to original rightmost-N behavior.
        if (rows.Count < 2)
        {
            return entries
                .OrderBy(x => x.Ocr.Box.X + x.Ocr.Box.W / 2.0)
                .Skip(Math.Max(0, entries.Count - banCount))
                .Select(x => x.CovenantName)
                .ToHashSet();
        }

        var secondRow = rows
            .OrderBy(r => r.Average(x => x.Ocr.Box.Y + x.Ocr.Box.H / 2.0))
            .Last();

        return secondRow
            .OrderBy(x => x.Ocr.Box.X + x.Ocr.Box.W / 2.0)
            .Skip(Math.Max(0, secondRow.Count - banCount))
            .Select(x => x.CovenantName)
            .ToHashSet();
    }

    private async Task<System.Drawing.Point?> FindSectionAnchorByGroupAsync(
        byte[] screenshot, int imgWidth, int imgHeight, string group)
    {
        var regions = _ocrStrategy.GetScanRegions();
        var scanRegion = regions.FirstOrDefault(r => r.Name == "BanSectionScan");
        if (scanRegion == null)
        {
            AppLogger.Warn("BanAnalyzer", "BanSectionScan region not found");
            return null;
        }

        if (!GroupHeaderText.TryGetValue(group, out var headerText))
        {
            AppLogger.Warn("BanAnalyzer", $"Unknown group '{group}'");
            return null;
        }

        var anchor = await FindSectionAnchorAsync(screenshot, imgWidth, imgHeight, scanRegion, headerText);
        if (anchor == null)
            AppLogger.Warn("BanAnalyzer", $"{group}: section header '{headerText}' not found");
        return anchor;
    }

    private async Task ScrollSectionTopToScreenTopAsync(
        string group,
        System.Drawing.Point? anchor,
        int imgHeight)
    {
        if (anchor == null)
        {
            AppConsole.WriteLine($"[BanAnalyzer] 警告：{group} 未找到标题，跳过滚动");
            return;
        }

        var hwnd = User32.FindArknightsWindow();
        if (hwnd == IntPtr.Zero)
        {
            AppLogger.Warn("BanAnalyzer", $"{group}: game window not found, cannot scroll");
            AppConsole.WriteLine($"[BanAnalyzer] 警告：找不到游戏窗口，{group} 滚动跳过");
            return;
        }

        int dy = ComputeAlignTopScrollDy(anchor.Value.Y, imgHeight);
        if (dy == 0)
        {
            AppLogger.Info("BanAnalyzer", $"{group}: anchor already near top, no scroll needed");
            return;
        }

        AppConsole.WriteLine($"[BanAnalyzer] → 将 {group} 顶端对齐屏幕顶端 (anchorY={anchor.Value.Y}, dy={dy})...");
        bool ok = _maaScroll.TryScrollAtWindowCenter(hwnd, dy);
        AppLogger.Info("BanAnalyzer",
            $"{group}: align-top scroll via MaaPipeline (anchorY={anchor.Value.Y}, dy={dy}) success={ok}");
        if (!ok)
            AppConsole.WriteLine($"[BanAnalyzer] 警告：{group} 对齐滚动失败");

        await Task.Delay(ScrollSettleMs);
        AppConsole.WriteLine($"[BanAnalyzer] ← {group} 顶端对齐滚动完成");
    }

    private int ComputeAlignTopScrollDy(int anchorY, int imgHeight)
    {
        if (anchorY <= 0 || imgHeight <= 0)
            return 0;

        int targetScrollPixels = Math.Max(0, anchorY - SectionTopPaddingPx);
        if (targetScrollPixels == 0)
            return 0;

        double pixelsPerClick = imgHeight * ScrollHalfScreenFraction / Math.Max(1, Math.Abs(ScrollClicks));
        if (pixelsPerClick <= 0)
            return 0;

        int clicks = (int)Math.Ceiling(targetScrollPixels / pixelsPerClick);
        return clicks <= 0 ? 0 : -(clicks * 120);
    }

    // ── Covenant name matching ────────────────────────────────────────────────

    /// <summary>
    /// Tries to map an OCR-recognised string to the closest known covenant name.
    ///
    /// Priority: exact → OCR contains name → name contains OCR → best char overlap (≥50%).
    /// Returns <c>null</c> when no candidate passes the threshold.
    /// </summary>
    private static string? MatchToCovenant(string ocrText, IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0) return null;

        var clean = ocrText.Trim();
        if (clean.Length == 0) return null;

        if (candidates.Contains(clean)) return clean;

        var containedInOcr = candidates.FirstOrDefault(
            c => clean.Contains(c, StringComparison.Ordinal));
        if (containedInOcr != null) return containedInOcr;

        var ocrInKnown = candidates.FirstOrDefault(
            c => c.Contains(clean, StringComparison.Ordinal));
        if (ocrInKnown != null) return ocrInKnown;

        var best = candidates
            .Select(c => (Name: c, Score: SharedCharCount(clean, c)))
            .Where(x => x.Score * 2 >= Math.Min(clean.Length, x.Name.Length))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        return best.Name;
    }

    private static int SharedCharCount(string a, string b)
    {
        var freq = new Dictionary<char, int>();
        foreach (var ch in b)
            freq[ch] = freq.GetValueOrDefault(ch) + 1;

        int shared = 0;
        foreach (var ch in a)
        {
            if (freq.TryGetValue(ch, out int count) && count > 0)
            {
                shared++;
                freq[ch] = count - 1;
            }
        }
        return shared;
    }

    // ── Section anchor detection ──────────────────────────────────────────────

    private async Task<System.Drawing.Point?> FindSectionAnchorAsync(
        byte[] screenshot, int imgWidth, int imgHeight,
        OcrRegionDefinition scanRegion, string headerText)
    {
        int sx = (int)(scanRegion.XPercent      * imgWidth);
        int sy = (int)(scanRegion.YPercent      * imgHeight);
        int sw = (int)(scanRegion.WidthPercent  * imgWidth);
        int sh = (int)(scanRegion.HeightPercent * imgHeight);

        var ocrResults = await _ocr.RecognizeRegionAsync(screenshot, sx, sy, sw, sh);
        var match = ocrResults.FirstOrDefault(r => r.Text.Contains(headerText));

        if (match == null)
        {
            AppLogger.Info("BanAnalyzer",
                $"FindSectionAnchor '{headerText}': not found " +
                $"({ocrResults.Count} results: [{string.Join(", ", ocrResults.Select(r => r.Text))}])");
            return null;
        }

        AppLogger.Info("BanAnalyzer",
            $"FindSectionAnchor '{headerText}': ({match.Box.X}, {match.Box.Y})");
        return new System.Drawing.Point(match.Box.X, match.Box.Y);
    }

    // ── Results output ────────────────────────────────────────────────────────

    private static void LogResults(BanScreenResult result, long elapsedMs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("══════════════ ban-screen analysis ══════════════");

        sb.Append("本场敌人: ");
        sb.AppendLine(result.EnemyFaction ?? "(未识别)");

        sb.AppendLine("核心盟约:");
        if (result.CoreCovenants.Count == 0)
        {
            sb.AppendLine("  (未识别)");
        }
        else
        {
            foreach (var c in result.CoreCovenants.OrderBy(c => c.X))
                sb.AppendLine($"  {c.Name,-12} {(c.IsBanned ? "【禁用】" : "  启用 ")}");
        }

        sb.AppendLine("附加盟约:");
        if (result.AdditionalCovenants.Count == 0)
        {
            sb.AppendLine("  (未识别)");
        }
        else
        {
            foreach (var c in result.AdditionalCovenants.OrderBy(c => c.X))
                sb.AppendLine($"  {c.Name,-12} {(c.IsBanned ? "【禁用】" : "  启用 ")}");
        }

        sb.Append($"耗时: {elapsedMs} ms");
        sb.AppendLine();
        sb.AppendLine("═════════════════════════════════════════════════");

        var output = sb.ToString();
        AppLogger.Info("BanAnalyzer", output);
        AppConsole.Write(output);
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static (int Width, int Height) GetPngDimensions(byte[] png)
    {
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }
}
