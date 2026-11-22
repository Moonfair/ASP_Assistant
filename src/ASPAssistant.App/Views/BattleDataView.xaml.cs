using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Views;

public partial class BattleDataView : UserControl
{
    private readonly BattleStatistics _statistics = new();
    private IReadOnlyDictionary<string, string> _names = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, Dictionary<string, string>> _chessNames = new Dictionary<string, Dictionary<string, string>>();
    private int? _bossRound;
    private readonly Dictionary<string, Dictionary<string, int>> _instancesByName = new(StringComparer.Ordinal);
    private CancellationTokenSource? _session;
    private GameTarget? _target;
    private string? _displaySourceId;
    private bool _waitingForCombatPhase;
    private bool _allowWindows, _sourceInitialized, _closed, _permissionDeclined;
    private bool? _memoryReadingAllowed;
    private readonly Func<uint, bool, Func<BattleFeedMessage, Task>, CancellationToken, Task> _connect;
    private readonly Func<GameTarget, Func<BattleUpdate, Task>, CancellationToken, Task> _connectMuMu;
    private BattleFeedState _state = BattleFeedState.WaitingForClient;
    private BattleFrame? _last;
    internal event Action<BattleUpdate>? Updated;

    public BattleDataView() : this(BattleDataSession.ConnectAsync) { }

    internal BattleDataView(Func<uint, bool, Func<BattleFeedMessage, Task>, CancellationToken, Task> connect,
        Func<GameTarget, Func<BattleUpdate, Task>, CancellationToken, Task>? connectMuMu = null)
    {
        _connect = connect;
        _connectMuMu = connectMuMu ?? MuMuBattleFeed.ConnectAsync;
        InitializeComponent();
    }

    internal void SetNames(IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, Dictionary<string, string>>? chessNames = null)
    {
        _names = names;
        if (chessNames != null) _chessNames = chessNames;
        _instancesByName.Clear();
        if (_last != null) RefreshRows();
    }

    internal void SetSource(GameTarget? target, bool allowWindows)
    {
        allowWindows &= target == null || target.Kind == GameTargetKind.Windows;
        if (_closed || (_sourceInitialized && _target == target && _allowWindows == allowWindows)) return;
        bool keepWorker = _session != null && _allowWindows && allowWindows;
        _sourceInitialized = true;
        // A temporarily missing window is not a switch to a different game.
        if (target != null && target.Id != _displaySourceId)
        {
            ClearDisplayedBattle();
            _waitingForCombatPhase = false;
            _displaySourceId = target.Id;
        }
        (_target, _allowWindows) = (target, allowWindows);
        if (keepWorker)
            Present(new(target == null ? BattleFeedState.WaitingForClient : BattleFeedState.Connecting));
        else Start();
    }

    internal void Stop()
    {
        _closed = true;
        _session?.Cancel();
        _session = null;
    }

    internal void SetMemoryReadingAllowed(bool allowed)
    {
        if (_closed || _memoryReadingAllowed == allowed) return;
        _memoryReadingAllowed = allowed;
        _permissionDeclined = false;
        if (!allowed)
        {
            _session?.Cancel();
            _session = null; // Discard callbacks that were already queued on the dispatcher.
            _waitingForCombatPhase = false;
            ClearDisplayedBattle();
            Present(new(BattleFeedState.Restricted));
        }
        else if (_sourceInitialized) Start();
    }

    internal void Reconnect()
    {
        _permissionDeclined = false;
        Start();
    }
    private void OnConnect(object sender, RoutedEventArgs e) => Reconnect();

    private void Start()
    {
        if (_closed || _memoryReadingAllowed != true) return;
        _session?.Cancel();
        _session = null;
        if (!_allowWindows && _target?.Kind != GameTargetKind.MuMu)
        {
            Present(new(BattleFeedState.WindowsOnly));
            return;
        }
        Present(new(BattleFeedState.Connecting));
        _session = new();
        _ = RunAsync(_target, !_permissionDeclined, _session);
    }

    private async Task RunAsync(GameTarget? target, bool mayRequestPermission, CancellationTokenSource session)
    {
        var token = session.Token;
        Task Publish(BattleFeedMessage message) => Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_session, session)) return;
            if (message.WindowPid != 0 && message.WindowPid != _target?.WindowPid) return;
            Present(message.Update);
        }, System.Windows.Threading.DispatcherPriority.Background, token).Task;
        try
        {
            await Task.Run(() => target is { Kind: GameTargetKind.MuMu }
                ? _connectMuMu(target, update => Publish(new(target.WindowPid, update)), token)
                : _connect(target?.WindowPid ?? 0, mayRequestPermission, Publish, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223 && target?.Kind != GameTargetKind.MuMu)
        {
            if (ReferenceEquals(_session, session))
            {
                _permissionDeclined = true;
                Present(new(BattleFeedState.PermissionRequired));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or InvalidOperationException)
        {
            AppLogger.Warn("BattleData", ex.Message);
            if (ReferenceEquals(_session, session)) Present(new(BattleFeedState.ConnectionFailed));
        }
        finally
        {
            if (ReferenceEquals(_session, session)) _session = null;
            session.Dispose();
        }
    }

    internal void Present(BattleUpdate update)
    {
        bool muMu = _target?.Kind == GameTargetKind.MuMu;
        if (muMu) update = update with { Source = GameTargetKind.MuMu };
        Updated?.Invoke(update);
        bool wasLive = _state == BattleFeedState.Live;
        var stage = update.GetStage();
        _state = ObserveBattlePhase(update, stage);
        // The server ranking and local damage frames update independently.
        PresentBossDamage(update, stage);
        FeedStatus.FontWeight = _state is BattleFeedState.PermissionRequired or BattleFeedState.ConnectionFailed
            or BattleFeedState.UnsupportedBuild or BattleFeedState.WindowsOnly ? FontWeights.SemiBold : FontWeights.Normal;
        ConnectButton.Visibility = _state is BattleFeedState.PermissionRequired or BattleFeedState.ConnectionFailed ||
            (muMu && _state == BattleFeedState.UnsupportedBuild)
            ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.Content = !muMu && _state == BattleFeedState.PermissionRequired ? "授权读取" : "重新连接";
        if (_state == BattleFeedState.Live && update.Sample is { } frame)
        {
            if (_last == null || _last.Round != frame.Round ||
                frame.Frame < _last.Frame || frame.GameSeconds < _last.GameSeconds)
                _instancesByName.Clear();
            else
            {
                // The operator table can disappear before the feed reports the end.
                if (frame.Operators.Count == 0 && _last.Operators.Count > 0)
                    frame = frame.Ended ? frame with { Operators = _last.Operators } : _last;
                if (wasLive && !frame.Ended && !_last.Ended && frame.Frame == _last.Frame &&
                    frame.Operators.Count == _last.Operators.Count) return;
            }
            _statistics.Update(frame);
            _last = frame;
            var clock = TimeSpan.FromSeconds(frame.GameSeconds);
            GameClock.Text = $"{(int)clock.TotalMinutes}:{clock.Seconds:D2}.{clock.Milliseconds / 100}";
            FeedStatus.Text = frame.Ended ? "本回合已结束" : _statistics.Rows.Count > 0 ? "本回合战斗数据" : "等待干员战斗数据";
            TotalDamage.Text = Number(_statistics.TotalDamage);
            Summary.Visibility = _statistics.Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshRows();
            return;
        }
        FeedStatus.Text = _state switch
        {
            BattleFeedState.Restricted => "DPS 统计已关闭。可在设置中解锁。",
            BattleFeedState.WaitingForRound when _last != null => "上回合数据 · 等待战斗",
            BattleFeedState.Connecting when muMu => "正在连接选中的 MuMu 实例…",
            BattleFeedState.WaitingForClient when muMu => "等待 MuMu 实例中的明日方舟",
            BattleFeedState.WaitingForRound when muMu => "已连接 MuMu，等待进入战斗。",
            BattleFeedState.PermissionRequired when muMu => "MuMu 未授予读取权限。请在模拟器中开启 Root 后重新连接。",
            BattleFeedState.UnsupportedBuild when muMu => "当前安卓客户端尚未适配。支持 ARM64 版 2.7.91。",
            BattleFeedState.ConnectionFailed when muMu => "MuMu 战斗采集未连接。请检查所选实例的 ADB，且只运行一个明日方舟客户端。",
            BattleFeedState.Connecting => "正在连接 PC 客户端…",
            BattleFeedState.WindowsOnly => "当前来源无法读取干员伤害。战斗统计支持官方 PC 客户端和 MuMu。",
            BattleFeedState.WaitingForRound => "已连接 PC 客户端，等待进入战斗。",
            BattleFeedState.PermissionRequired => "Windows 需要授权才能读取战斗数据。",
            BattleFeedState.UnsupportedBuild => "当前客户端版本尚未适配。已验证的 PC 版本为 2.7.91。",
            BattleFeedState.ReadInterrupted => "正在重新同步战斗数据…",
            BattleFeedState.ConnectionFailed => "战斗数据连接已中断。",
            _ => "等待官方 PC 客户端"
        };
        if ((_last != null || BossRows.Items.Count > 0) && _state is (BattleFeedState.Connecting or
            BattleFeedState.WaitingForClient or BattleFeedState.PermissionRequired or BattleFeedState.UnsupportedBuild or
            BattleFeedState.ReadInterrupted or BattleFeedState.ConnectionFailed or BattleFeedState.WindowsOnly))
            FeedStatus.Text += " 当前显示最近一次读数。";
    }

    private BattleFeedState ObserveBattlePhase(BattleUpdate update, BattleStage? stage)
    {
        // The same controller can keep publishing Live frames throughout preparation.
        var phase = stage?.State;
        bool newFrameOnlyRound = stage == null && update is { State: BattleFeedState.Live, Garrison: null,
            GarrisonInterrupted: false, Sample: { Ended: false } sample } && _last?.Round != sample.Round;
        if (phase is 4 or 5 or 6 || newFrameOnlyRound) // Battle, help battle, boss battle, or frame-only feed
        {
            if (_waitingForCombatPhase) ClearDisplayedBattle();
            _waitingForCombatPhase = false;
        }
        else if (phase is >= 0 and <= 14 and not 9) // Pause is not a round boundary.
            _waitingForCombatPhase = true;

        if (_waitingForCombatPhase && update.State == BattleFeedState.Live)
        {
            // Accept the final correction for the displayed round, never a preparation frame.
            if (update.Sample is not { Ended: true, Operators.Count: > 0 } final || _last?.Round != final.Round)
                return BattleFeedState.WaitingForRound;
        }
        return update.State;
    }

    private void ClearDisplayedBattle()
    {
        ClearBossDamage();
        _last = null;
        _statistics.Reset();
        _instancesByName.Clear();
        OperatorRows.ItemsSource = null;
        Summary.Visibility = Visibility.Collapsed;
        TotalDamage.Text = "—";
        GameClock.Text = "";
    }

    private void PresentBossDamage(BattleUpdate update, BattleStage? stage)
    {
        if (stage is not { State: GarrisonPhase.BossBattle })
        {
            if (_bossRound.HasValue && (stage?.State is 4 or 5 ||
                (stage == null && update is { State: BattleFeedState.Live, Garrison: null, GarrisonInterrupted: false, Sample: { Ended: false } frame }
                    && _last != null && frame.Round != _last.Round)))
                ClearBossDamage();
            return; // Settlement, waiting and transient read errors keep the last board.
        }
        if (_bossRound != stage.Round)
        {
            ClearBossDamage();
            _bossRound = stage.Round;
        }
        BossSummary.Visibility = Visibility.Visible;
        if (stage.BossDamage is not { Rows.Length: > 0 } board) return;
        var rows = board.Rows.OrderByDescending(r => r.Damage).Take(3).Select(r =>
        {
            var self = r.PlayerIndex == stage.PlayerIndex;
            var selected = self ? update.Garrison?.SelectedCharacters :
                update.Garrison?.Teammates?.FirstOrDefault(t => t.PlayerIndex == r.PlayerIndex)?.SelectedCharacters;
            var name = GarrisonChessLabel.Resolve(r.ChessId, stage.ActivityId, selected, _chessNames, _names);
            var player = string.IsNullOrWhiteSpace(r.PlayerName) ? (self ? "自己" : $"玩家 {r.PlayerIndex}") : r.PlayerName;
            var damage = Number(r.Damage);
            var percent = board.Percent(r.Damage) is { } value ? $"{value}%" : "—";
            return new BossDisplayRow(name, player, damage, percent,
                $"{player} · {name}\n对领袖伤害 {damage}　占领袖血量 {percent}");
        }).ToArray();
        if (BossRows.ItemsSource is not BossDisplayRow[] previous || !rows.SequenceEqual(previous))
            BossRows.ItemsSource = rows;
        BossPending.Visibility = Visibility.Collapsed;
    }

    private void ClearBossDamage()
    {
        _bossRound = null;
        BossSummary.Visibility = Visibility.Collapsed;
        BossPending.Visibility = Visibility.Visible;
        BossRows.ItemsSource = null;
    }

    private sealed record BossDisplayRow(string Name, string Player, string Damage, string Percent, string Details);

    private string? CatalogName(string id)
    {
        if (_names.TryGetValue(id, out var name)) return name;
        // Keep the full source key for statistics; strip an instance suffix only for catalog lookup.
        int separator = id.IndexOfAny(['#', '.', '-']);
        if (separator > 0 && _names.TryGetValue(id[..separator], out name)) return name;
        int underscore = id.LastIndexOf('_');
        return underscore > 0 && uint.TryParse(id.AsSpan(underscore + 1), out _) &&
            _names.TryGetValue(id[..underscore], out name) ? name : null;
    }

    private void RefreshRows()
    {
        var rows = _statistics.Rows.Select(row => (Row: row, Name: CatalogName(row.Id))).ToArray();
        // Assign once per round in source-key order, not damage-rank order.
        foreach (var (row, name) in rows.OrderBy(r => r.Row.Id, StringComparer.Ordinal))
        {
            if (name == null) continue;
            if (!_instancesByName.TryGetValue(name, out var ids))
                _instancesByName.Add(name, ids = new(StringComparer.Ordinal));
            ids.TryAdd(row.Id, ids.Count + 1);
        }
        OperatorRows.ItemsSource = rows.Select(r => DisplayRow(r.Row, r.Name)).ToArray();
    }

    private BattleDisplayRow DisplayRow(BattleOperatorRow row, string? catalogName)
    {
        var name = catalogName ?? row.Id;
        var instance = catalogName != null && _instancesByName[catalogName] is { Count: > 1 } ids
            ? $"#{ids[row.Id]}" : "";
        var c = row.Counters;
        var label = instance.Length > 0 ? $"{name} {instance}" : name;
        var details = $"{label}\n物理 {Number(c.Physical)}　法术 {Number(c.Arts)}\n真实 {Number(c.TrueDamage)}　元素 {Number(c.Elemental)}";
        return new(name, instance, catalogName == null ? "" : $"icons/operators/{catalogName}.png", Number(c.Damage), row.DamageShare, details);
    }

    private static string Number(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private sealed record BattleDisplayRow(string Name, string Instance, string IconPath, string Damage, double DamageShare, string Details);
}
