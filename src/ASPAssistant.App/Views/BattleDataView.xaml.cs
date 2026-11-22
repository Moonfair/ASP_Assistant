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
    private CancellationTokenSource? _session;
    private GameTarget? _target;
    private bool _allowWindows, _sourceInitialized, _closed, _permissionDeclined;
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

    internal void SetNames(IReadOnlyDictionary<string, string> names) => _names = names;

    internal void SetSource(GameTarget? target, bool allowWindows)
    {
        allowWindows &= target == null || target.Kind == GameTargetKind.Windows;
        if (_closed || (_sourceInitialized && _target == target && _allowWindows == allowWindows)) return;
        bool keepWorker = _session != null && _allowWindows && allowWindows;
        _sourceInitialized = true;
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

    internal void Reconnect()
    {
        _permissionDeclined = false;
        Start();
    }
    private void OnConnect(object sender, RoutedEventArgs e) => Reconnect();

    private void Start()
    {
        if (_closed) return;
        _session?.Cancel();
        _session = null;
        _last = null;
        _statistics.Reset();
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
        _state = update.State;
        Updated?.Invoke(update);
        FeedStatus.FontWeight = _state is BattleFeedState.PermissionRequired or BattleFeedState.ConnectionFailed
            or BattleFeedState.UnsupportedBuild or BattleFeedState.WindowsOnly ? FontWeights.SemiBold : FontWeights.Normal;
        ConnectButton.Visibility = _state is BattleFeedState.PermissionRequired or BattleFeedState.ConnectionFailed ||
            (muMu && _state == BattleFeedState.UnsupportedBuild)
            ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.Content = !muMu && _state == BattleFeedState.PermissionRequired ? "授权读取" : "重新连接";
        if (_state == BattleFeedState.Live && update.Sample is { } frame)
        {
            if (!frame.Ended && _last is { } old && old.Round == frame.Round && old.Frame == frame.Frame) return;
            _statistics.Update(frame);
            _last = frame;
            var clock = TimeSpan.FromSeconds(frame.GameSeconds);
            GameClock.Text = $"{(int)clock.TotalMinutes}:{clock.Seconds:D2}.{clock.Milliseconds / 100}";
            FeedStatus.Text = frame.Ended ? "本回合已结束" : _statistics.Rows.Count > 0 ? "本回合战斗数据" : "等待干员战斗数据";
            TotalDamage.Text = _statistics.Rows.Count == 0 ? "—" : Number(_statistics.TotalDamage);
            Summary.Visibility = _statistics.Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            OperatorRows.ItemsSource = _statistics.Rows.Select(DisplayRow).ToArray();
            return;
        }
        if (_state == BattleFeedState.WaitingForRound && _last?.Ended == true)
        {
            FeedStatus.Text = "上回合数据 · 等待战斗";
            return;
        }
        _last = null;
        _statistics.Reset();
        OperatorRows.ItemsSource = null;
        Summary.Visibility = Visibility.Collapsed;
        TotalDamage.Text = "—";
        GameClock.Text = "—";
        FeedStatus.Text = _state switch
        {
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
    }

    private BattleDisplayRow DisplayRow(BattleOperatorRow row)
    {
        var name = _names.GetValueOrDefault(row.Id, row.Id);
        var c = row.Counters;
        var details = $"{name}\n物理 {Number(c.Physical)}　法术 {Number(c.Arts)}\n真实 {Number(c.TrueDamage)}　元素 {Number(c.Elemental)}";
        return new(name, Number(c.Damage), row.DamageShare, details);
    }

    private static string Number(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private sealed record BattleDisplayRow(string Name, string Damage, double DamageShare, string Details);
}
