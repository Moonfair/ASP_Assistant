using System.Collections.Frozen;
using System.Drawing;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public enum PreparationPhase { None, Information, Strategy, Squad, CovenantOverview }
public enum PreparationNoticeKind { Banner, Result, ScrollRequired, Dismiss }

public sealed record PreparationNotice(string Text, PreparationNoticeKind Kind, bool IsNewResult = false);

public record BanCandidate(string Name, IReadOnlyList<string> CoreCovenants,
    IReadOnlyList<string> AdditionalCovenants, IReadOnlyList<string> TemplateNames);

/// <summary>Shared state and phase gating for automatic and button-triggered BP scans.</summary>
public sealed class PreparationScanner(
    IOcrEngine ocr,
    IReadOnlyList<BanCandidate> candidates,
    TimeProvider? timeProvider = null,
    CovenantCatalog? covenantCatalog = null,
    PreparationEnemyRecognizer? enemyRecognizer = null)
{
    private readonly object _stateLock = new();
    private readonly HashSet<string> _banned = [];
    private readonly HashSet<string> _manualAllowed = [];
    private readonly HashSet<string> _manualBanned = [];
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private string? _lastFingerprint;
    private bool _automaticCleared;
    private DateTimeOffset? _awaySince;
    private DateTimeOffset? _lastPreparationSeen;
    private PreparationPhase _lastPreparationPhase;
    private GroupedRound? _groupedRound;
    private bool _sessionStarted;
    private int _generation;
    private CovenantAvailability? _covenantAvailability;
    private PreparationEnemyInfo? _enemyInfo;
    public PreparationEnemyInfo? EnemyInfo { get { lock (_stateLock) return _enemyInfo; } }
    public event Action<PreparationEnemyInfo?>? EnemyInfoChanged;
    public CovenantAvailability? CovenantAvailability { get { lock (_stateLock) return _covenantAvailability; } }
    public event Action<CovenantAvailability?>? CovenantAvailabilityChanged;
    public PreparationPhase Phase { get; private set; }
    private PreparationNotice _notice = new("自动 BP 检测已开启，等待准备界面", PreparationNoticeKind.Banner);
    public string Status => _notice.Text;
    public IReadOnlyList<string> BannedNames { get { lock (_stateLock) return _banned.ToList(); } }
    public int Generation { get { lock (_stateLock) return _generation; } }
    public event Action<IReadOnlyList<string>>? BansChanged;
    public event Action<PreparationNotice>? StatusChanged;

    public static bool ShouldBan(BanCandidate candidate, IReadOnlyCollection<string> enabledCore,
        IReadOnlyCollection<string> enabledAdditional) =>
        candidate.CoreCovenants.Count > 0
            ? !candidate.CoreCovenants.Any(enabledCore.Contains)
            : !candidate.AdditionalCovenants.Any(enabledAdditional.Contains);

    public static PreparationPhase Classify(IEnumerable<string> texts)
    {
        var text = Regex.Replace(string.Join("", texts), @"\s+", "");
        if (text.Contains("确定自选编队")) return PreparationPhase.Squad;
        if (text.Contains("选择策略")) return PreparationPhase.Strategy;
        if (text.Contains("确认本局信息")) return PreparationPhase.Information;
        return PreparationPhase.None;
    }

    public async Task ProcessAsync(byte[] png, bool force = false, int? expectedGeneration = null)
    {
        int requestGeneration = Generation;
        if (expectedGeneration.HasValue && expectedGeneration != Generation) return;
        using var stream = new MemoryStream(png);
        using var image = new Bitmap(stream);
        int width = image.Width, height = image.Height;
        var heading = await ocr.RecognizeRegionAsync(png, 0, 0, (int)(width * .74), (int)(height * .15));
        if (requestGeneration != Generation) return;
        Phase = Classify(heading.Select(t => t.Text));
        var now = _clock.GetUtcNow();
        if (Phase == PreparationPhase.None)
        {
            _awaySince ??= now;
            if (await TryProcessOverviewAsync(png, image, heading, requestGeneration, force)) return;
            if (requestGeneration == Generation) NotifyCaptureUnavailable();
            return;
        }

        var titleText = Regex.Replace(string.Concat(heading.Select(t => t.Text)), @"\s+", "");
        var mode = covenantCatalog?.ModePools.OrderByDescending(entry => entry.Key.Length)
            .FirstOrDefault(entry => titleText.Contains(entry.Key, StringComparison.Ordinal));
        var modeName = mode?.Key;
        bool awayTooLong = _awaySince.HasValue && now - _awaySince.Value >= TimeSpan.FromSeconds(30);
        bool nextGroupedRound = _groupedRound != null && (
            (Phase == PreparationPhase.Information && _lastPreparationPhase is PreparationPhase.Strategy or PreparationPhase.Squad)
            || (modeName != null && modeName != _groupedRound.Mode)
            || (_lastPreparationSeen.HasValue && now - _lastPreparationSeen.Value >= TimeSpan.FromSeconds(30)));
        bool nextRound = _sessionStarted && awayTooLong;
        if (nextRound || nextGroupedRound) ResetRound();
        if (nextRound || (Phase == PreparationPhase.Information &&
            _lastPreparationPhase is PreparationPhase.Strategy or PreparationPhase.Squad))
            SetEnemyInfo(null, Generation);

        _lastPreparationSeen = now;
        _lastPreparationPhase = Phase;
        _sessionStarted = true;
        _awaySince = null;
        int generation = Generation;
        if (Phase != PreparationPhase.Information)
        {
            SetStatus($"{(Phase == PreparationPhase.Strategy ? "选择策略" : "自选编队")} · 已保留 {BannedNames.Count} 名禁用干员",
                PreparationNoticeKind.Dismiss);
            return;
        }
        if (enemyRecognizer != null)
        {
            var previous = EnemyInfo;
            if (force || previous is not { Boss: not null, Battlefield: not null, EnemyTypes.Count: 3 } ||
                (modeName is not null && previous.Mode != modeName))
            {
                var read = await enemyRecognizer.ReadAsync(png, image, modeName, force ? null : previous?.Battlefield);
                if (generation != Generation) return;
                if (read != null)
                {
                    if ((read.Boss != null && previous?.Boss != null && read.Boss != previous.Boss) ||
                        (read.Battlefield != null && previous?.Battlefield != null && read.Battlefield != previous.Battlefield) ||
                        (read.Mode != null && previous?.Mode != null && read.Mode != previous.Mode))
                        previous = null;
                    SetEnemyInfo(read with
                    {
                        Boss = read.Boss ?? previous?.Boss,
                        Battlefield = read.Battlefield ?? previous?.Battlefield,
                        Mode = read.Mode ?? previous?.Mode,
                        EnemyTypes = read.EnemyTypes.Count >= (previous?.EnemyTypes.Count ?? 0)
                            ? read.EnemyTypes : previous!.EnemyTypes
                    }, generation);
                }
            }
        }
        var pool = mode?.Value ?? _groupedRound?.Pool;
        if (pool?.SelectCoreGroup == true)
        {
            await ProcessGroupedAsync(png, image, mode?.Key ?? _groupedRound!.Mode, pool, generation, force);
            return;
        }

        await ProcessCovenantsAsync(png, image, pool, generation, force);
    }

    private async Task<bool> TryProcessOverviewAsync(byte[] png, Bitmap image,
        IReadOnlyList<OcrTextResult> heading, int generation, bool force)
    {
        int width = image.Width, height = image.Height;
        bool hasLogo = heading.Any(t => t.Box.X < width * .25 &&
            Regex.Replace(t.Text, @"\s+", "").Contains("STRONGHOLDPROTOCOL", StringComparison.OrdinalIgnoreCase));
        (OcrTextResult CoreTitle, OcrTextResult AdditionalTitle)? section = null;
        if (!hasLogo)
        {
            if (heading.Any(t => t.Text.Contains("回合") || t.Text.Trim() == "商店")) return false;
            // The small English logo can be unreadable in windowed mode. Probe the left edge for
            // the two Chinese headings and use the ban-list heading to confirm the overview below.
            var titles = await ocr.RecognizeRegionAsync(png, 0, (int)(height * .14),
                (int)(width * .24), (int)(height * .40));
            if (generation != Generation) return false;
            section = CoreCovenantRecognizer.FindSection(titles, height);
            if (!IsAligned(section)) return false;
        }

        var texts = await ocr.RecognizeRegionAsync(png, 0, (int)(height * .14),
            (int)(width * .70), (int)(height * .80));
        if (generation != Generation) return false;
        var fullSection = CoreCovenantRecognizer.FindSection(texts, height);
        if (fullSection == null) return false;
        section ??= fullSection;
        if (!IsAligned(section) || section is not { } titlesFound) return false;

        var grid = CovenantGrid.Overview(titlesFound.CoreTitle, titlesFound.AdditionalTitle);
        int additionalTop = titlesFound.AdditionalTitle.Box.Y;
        var banTitle = texts.FirstOrDefault(t => t.Text.Contains("本局禁用干员") &&
            t.Box.Y > additionalTop + grid.FirstAdditionalLabel && t.Box.X < width * .25);
        if (!hasLogo && banTitle == null) return false;

        // A ban list immediately after the first additional row bounds the shorter mode-specific grid.
        var pool = banTitle != null && banTitle.Box.Y < additionalTop + grid.FirstAdditionalLabel + grid.AdditionalRowSpacing - grid.IconToLabel
            ? FindSingleRowOverviewPool(texts, grid, additionalTop, height) : null;
        Phase = PreparationPhase.CovenantOverview;
        _sessionStarted = true;
        await ProcessCovenantsAsync(png, image, pool, generation, force, (texts, grid));
        return true;

        bool IsAligned((OcrTextResult CoreTitle, OcrTextResult AdditionalTitle)? titles)
        {
            if (titles is not { } pair) return false;
            var core = pair.CoreTitle.Box;
            var additional = pair.AdditionalTitle.Box;
            return Math.Abs(core.X - additional.X) < width * .03 ||
                // OCR can include the icon to the left of the core heading in its box.
                (core.X < additional.X && Math.Abs(core.X + core.W - additional.X - additional.W) < width * .025);
        }
    }

    private CovenantPool? FindSingleRowOverviewPool(IReadOnlyList<OcrTextResult> texts, CovenantGrid grid,
        int additionalTop, int height)
    {
        if (covenantCatalog == null) return null;
        var core = ReadLabels(additionalTop - grid.CoreLabelToAdditionalTitle, covenantCatalog.CoreCovenants);
        var additional = ReadLabels(additionalTop + grid.FirstAdditionalLabel, covenantCatalog.AdditionalCovenants);
        return covenantCatalog.ModePools.Values.FirstOrDefault(pool =>
            core.SetEquals(pool.CoreCovenants) && additional.SetEquals(pool.AdditionalCovenants));

        HashSet<string> ReadLabels(double labelY, IReadOnlyList<string> names) => texts
            .Where(t => Math.Abs(t.Box.Y + t.Box.H / 2.0 - labelY) <= height * .035)
            .SelectMany(t => CovenantLabelMatcher.Find(Regex.Replace(t.Text, @"\s+", ""), names))
            .Select(match => match.Name).ToHashSet();
    }

    private async Task ProcessCovenantsAsync(byte[] png, Bitmap image, CovenantPool? pool, int generation, bool force,
        (IReadOnlyList<OcrTextResult> Texts, CovenantGrid Grid)? overview = null)
    {
        int width = image.Width, height = image.Height;
        var grid = overview?.Grid ?? CovenantGrid.Preparation(width, height);
        var texts = overview?.Texts ?? await ocr.RecognizeRegionAsync(png, 0, (int)(height * .15),
            (int)(width * .70), (int)(height * .38));
        if (generation != Generation) return;
        var fullCore = covenantCatalog?.CoreCovenants is { Count: > 0 } configuredCore
            ? configuredCore.ToArray() : candidates.SelectMany(c => c.CoreCovenants).Distinct().ToArray();
        var fullAdditional = covenantCatalog?.AdditionalCovenants is { Count: > 0 } configuredAdditional
            ? configuredAdditional.ToArray() : candidates.SelectMany(c => c.AdditionalCovenants).Distinct().ToArray();
        var coreNames = pool?.CoreCovenants ?? fullCore;
        var disabled = CoreCovenantRecognizer.FindDisabled(image, texts, coreNames, grid)
            ?? await CoreCovenantRecognizer.RereadDisabledAsync(image, texts, coreNames, ocr, grid);
        if (generation != Generation) return;
        if (disabled == null)
        {
            SetStatus($"核心盟约识别不完整 · 请显示顶部完整核心盟约；保留 {BannedNames.Count} 名禁用干员");
            return;
        }
        var additionalNames = pool?.AdditionalCovenants ?? fullAdditional;
        var additionalTexts = additionalNames.Count == 0 ? [] : overview?.Texts ?? await ocr.RecognizeRegionAsync(png,
            0, (int)(height * .45), (int)(width * .70), (int)(height * .51));
        if (generation != Generation) return;
        var disabledAdditional = AdditionalCovenantRecognizer.FindDisabled(image, additionalTexts, additionalNames, grid);
        if (disabledAdditional == null)
        {
            SetStatus($"附加盟约识别不完整 · 请显示完整盟约区域；保留 {BannedNames.Count} 名禁用干员");
            return;
        }
        PublishBans(coreNames, additionalNames, disabled, disabledAdditional, generation, force);
    }

    private async Task ProcessGroupedAsync(byte[] png, Bitmap image, string mode, CovenantPool pool,
        int generation, bool force)
    {
        var texts = await ocr.RecognizeRegionAsync(png, 0, (int)(image.Height * .15),
            (int)(image.Width * .70), (int)(image.Height * .815));
        if (generation != Generation) return;

        var round = _groupedRound ??= new(mode, pool);
        var selection = GroupedCoreRecognizer.Read(image, texts, pool.CoreCovenants);
        if (selection?.Enabled is { } enabled)
        {
            // Choosing the other side changes which group is active, but keeps the same partition.
            var selectedNames = enabled.ToHashSet();
            if (round.EnabledCore is { } previous && !selectedNames.SetEquals(previous)
                && !selectedNames.SetEquals(pool.CoreCovenants.Except(previous)))
            {
                ResetRound();
                generation = Generation;
                round = _groupedRound = new(mode, pool);
            }
            round.EnabledCore = enabled;
            round.ConfirmedSide = selection.Side;
        }
        if (selection != null) round.ObservedSide = selection.Side;

        round.DisabledAdditional ??= AdditionalCovenantRecognizer.FindDisabled(image, texts, pool.AdditionalCovenants);
        if (round.EnabledCore == null || round.ObservedSide != round.ConfirmedSide)
        {
            SetStatus(round.DisabledAdditional == null
                ? "请显示顶部两组核心盟约，确认当前选中的一组"
                : "附加盟约已锁定 · 请向上拖回核心盟约，显示当前选中的一组");
            return;
        }
        if (round.DisabledAdditional == null)
        {
            SetStatus($"已记录核心组：{string.Join("、", round.EnabledCore)} · 请手动向下拖动游戏左侧滚动条，显示全部附加盟约",
                PreparationNoticeKind.ScrollRequired);
            return;
        }

        PublishBans(pool.CoreCovenants, pool.AdditionalCovenants,
            pool.CoreCovenants.Except(round.EnabledCore).ToArray(), round.DisabledAdditional,
            generation, force, grouped: true);
    }

    private void PublishBans(IReadOnlyList<string> coreNames, IReadOnlyList<string> additionalNames,
        IReadOnlyList<string> disabled, IReadOnlyList<string> disabledAdditional, int generation, bool force,
        bool grouped = false)
    {
        var enabledCore = coreNames.Except(disabled).ToHashSet();
        var enabledAdditional = additionalNames.Except(disabledAdditional).ToHashSet();
        var fingerprint = $"C:{string.Join("|", enabledCore.Order())};A:{string.Join("|", enabledAdditional.Order())}";
        bool changed = false;
        bool automaticCleared;
        bool newResult;
        lock (_stateLock)
        {
            if (generation != _generation) return;
            newResult = force || fingerprint != _lastFingerprint;
            if (newResult)
            {
                _lastFingerprint = fingerprint;
                _automaticCleared = false;
                _covenantAvailability = new(enabledCore.ToFrozenSet(), enabledAdditional.ToFrozenSet());
                var next = candidates.Where(c => ShouldBan(c, enabledCore, enabledAdditional)).Select(c => c.Name).ToHashSet();
                next.UnionWith(_manualBanned);
                next.ExceptWith(_manualAllowed);
                changed = !_banned.SetEquals(next);
                _banned.Clear();
                _banned.UnionWith(next);
            }
            automaticCleared = _automaticCleared;
        }
        if (changed) BansChanged?.Invoke(BannedNames);
        if (newResult) CovenantAvailabilityChanged?.Invoke(CovenantAvailability);
        if (automaticCleared)
        {
            SetStatus("自动禁用已清空 · 点击截屏录入可重扫盟约", PreparationNoticeKind.Dismiss);
            return;
        }
        SetStatus($"禁用核心：{(disabled.Count == 0 ? "无" : string.Join("、", disabled))}；禁用附加：{(disabledAdditional.Count == 0 ? "无" : string.Join("、", disabledAdditional))}"
            + (grouped ? " · 附加盟约已锁定" : "") + $" · 已禁用 {BannedNames.Count} 名干员",
            PreparationNoticeKind.Result, newResult);
    }

    private void ResetRound()
    {
        _groupedRound = null;
        _lastFingerprint = null;
        ClearBans();
    }

    private void SetEnemyInfo(PreparationEnemyInfo? info, int generation)
    {
        lock (_stateLock)
        {
            if (generation != _generation) return;
            if (info == _enemyInfo || (info != null && _enemyInfo != null &&
                info.Boss == _enemyInfo.Boss && info.Battlefield == _enemyInfo.Battlefield &&
                info.Mode == _enemyInfo.Mode && info.EnemyTypes.SequenceEqual(_enemyInfo.EnemyTypes))) return;
            _enemyInfo = info;
        }
        EnemyInfoChanged?.Invoke(info);
    }

    private sealed class GroupedRound(string mode, CovenantPool pool)
    {
        public string Mode { get; } = mode;
        public CovenantPool Pool { get; } = pool;
        public IReadOnlyList<string>? EnabledCore { get; set; }
        public int? ConfirmedSide { get; set; }
        public int? ObservedSide { get; set; }
        public IReadOnlyList<string>? DisabledAdditional { get; set; }
    }

    public void SetManualBan(string name, bool banned)
    {
        lock (_stateLock)
        {
            if (banned) { _manualAllowed.Remove(name); _manualBanned.Add(name); _banned.Add(name); }
            else { _manualAllowed.Add(name); _manualBanned.Remove(name); _banned.Remove(name); }
        }
        BansChanged?.Invoke(BannedNames);
    }

    public void ClearBans()
    {
        lock (_stateLock)
        {
            _generation++;
            _banned.Clear();
            _manualAllowed.Clear();
            _manualBanned.Clear();
            _automaticCleared = true;
            _covenantAvailability = null;
        }
        BansChanged?.Invoke(BannedNames);
        CovenantAvailabilityChanged?.Invoke(CovenantAvailability);
        SetStatus("禁用名单已清空 · 盟约变化后自动更新，或点击截屏录入重扫", PreparationNoticeKind.Dismiss);
    }

    public void NotifyCaptureUnavailable() =>
        SetStatus($"自动 BP 检测中 · 已录入 {BannedNames.Count} 名禁用干员", PreparationNoticeKind.Dismiss);

    internal void ResetTarget()
    {
        _awaySince = null;
        _lastPreparationSeen = null;
        _lastPreparationPhase = PreparationPhase.None;
        _sessionStarted = false;
        Phase = PreparationPhase.None;
        ResetRound();
        SetEnemyInfo(null, Generation);
        SetStatus("自动 BP 检测已开启，等待准备界面");
    }

    private void SetStatus(string text, PreparationNoticeKind kind = PreparationNoticeKind.Banner, bool newResult = false)
    {
        var notice = new PreparationNotice(text, kind, newResult);
        if (_notice == notice && !newResult) return;
        if (Status != text) AppLogger.Info("Preparation", text);
        _notice = notice;
        StatusChanged?.Invoke(notice);
    }
}
