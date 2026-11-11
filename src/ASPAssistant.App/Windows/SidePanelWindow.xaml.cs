using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ASPAssistant.App.Services;
using ASPAssistant.Core;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow : Window
{
    public OperatorBrowseViewModel OperatorBrowseVm { get; }
    public EquipmentBrowseViewModel EquipmentBrowseVm { get; }
    public TrackingViewModel TrackingVm { get; }
    public GameStateViewModel GameStateVm { get; }
    public BanViewModel BanVm { get; }

    public event Action? ManualScanRequested;
    public event Action? ClearBansRequested;
    public event Action<string, bool>? BanToggleRequested;

    private UpdateService? _updateService;
    private UpdateInfo? _pendingUpdate;
    private bool _checkingForUpdates;
    private bool _updateCheckAttempted;
    private readonly DispatcherTimer _preparationResultTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _waitingForScroll;
    private LineupLibraryViewModel? _lineupLibrary;
    private List<Operator> _lineupOperators = [];
    private List<Equipment> _lineupEquipment = [];
    private IReadOnlyList<Strategy> _lineupStrategies = [];
    private LineupEditorWindow? _lineupEditor;

    // 用户意图追踪
    private bool _isUserPositioned;       // 用户手动移动过窗口
    private bool _isUserSized;            // 用户手动调整过窗口高度
    private bool _isProgrammaticChange;   // 程序正在设置位置/尺寸，抑制事件响应
    private bool _isRestoring;            // 正在从最小化恢复，抑制事件响应
    private WindowState _previousWindowState;
    // WPF 初始 Show() 会触发 SizeChanged/LocationChanged，在首次 UpdatePosition 前清除误设的标志
    private bool _layoutInitialized;
    private (RECT Bounds, bool AttachInside)? _trackedGame;

    public SidePanelWindow(
        OperatorBrowseViewModel operatorVm,
        EquipmentBrowseViewModel equipmentVm,
        TrackingViewModel trackingVm,
        GameStateViewModel gameStateVm,
        BanViewModel banVm)
    {
        OperatorBrowseVm = operatorVm;
        EquipmentBrowseVm = equipmentVm;
        TrackingVm = trackingVm;
        GameStateVm = gameStateVm;
        BanVm = banVm;

        InitializeComponent();
        InitializeFilterLayout();
        _preparationResultTimer.Tick += (_, _) => HidePreparationOverlay();
        Closed += (_, _) => _preparationResultTimer.Stop();
        Closing += (_, args) =>
        {
            if (LineupView.IsImporting && _lineupLibrary is { } library)
            {
                args.Cancel = true;
                library.Status = "正在导入阵容，请稍候再关闭。";
                return;
            }
            if (_lineupEditor is { } editor && !editor.ConfirmClose())
            {
                args.Cancel = true;
                editor.BringForward();
            }
        };
        Closed += (_, _) =>
        {
            _lineupEditor?.ApprovePanelClose();
            Application.Current.Shutdown();
        };

        LocationChanged += OnLocationChanged;
        SizeChanged     += OnSizeChanged;
        StateChanged    += OnStateChanged;

        OperatorView.DataContext = operatorVm;
        EquipmentView.DataContext = equipmentVm;
        TrackingView.DataContext = trackingVm;

        OperatorView.IsTrackedCheck = trackingVm.IsTracked;
        EquipmentView.IsTrackedCheck = trackingVm.IsTracked;

        OperatorView.IsBannedCheck = banVm.IsBanned;

        OperatorView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        OperatorView.UntrackingRequested += name => trackingVm.RemoveTracking(name);
        EquipmentView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        EquipmentView.UntrackingRequested += name => trackingVm.RemoveTracking(name);

        OperatorView.ClearBansRequested += () => ClearBansRequested?.Invoke();
        OperatorView.ManualScanRequested += () => ManualScanRequested?.Invoke();
        OperatorView.BanToggleRequested += (name, banned) => BanToggleRequested?.Invoke(name, banned);

        banVm.BansChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                OperatorView.RefreshBannedStates();
                operatorVm.NotifyBansChanged();
                LineupView.RefreshBannedPortraits();
            });
        };

        trackingVm.TrackingChanged += () =>
        {
            OperatorView.RefreshTrackingStates();
            EquipmentView.RefreshTrackingStates();
        };
    }

    public void ConfigureLineups(LineupLibraryViewModel library, List<Operator> operators, List<Equipment> equipment,
        IReadOnlyList<Strategy>? strategies = null)
    {
        _lineupLibrary = library;
        _lineupOperators = operators;
        _lineupEquipment = equipment;
        _lineupStrategies = strategies ?? [];
        LineupView.ConfigurePortraits(FindLineupOperatorIcon, BanVm.IsBanned, FindLineupStrategyIcon);
        LineupView.DataContext = library;
        LineupView.ShareService = new LineupShareService(operators.Select(o => o.Name), equipment.Select(e => e.Name));
        LineupView.NewRequested += () => TryOpenLineupEditor(null);
        LineupView.EditRequested += lineup => TryOpenLineupEditor(lineup);
        LineupView.PreviewRequested += OpenLineupPreview;
        LineupView.TrackRequested += lineup =>
        {
            var added = TrackingVm.TrackLineup(lineup);
            library.Status = added > 0 ? $"已加入 {added} 项干员或装备追踪。" : "这个阵容的干员和装备已全部在追踪列表中。";
        };
    }

    private LineupSiteClient? _lineupSite;

    internal void ConfigureLineupSite(LineupSiteClient site)
    {
        _lineupSite = site;
        LineupView.ConfigureSite(site);
        SettingsLineupAccountCard.Visibility = Visibility.Visible;
        RefreshSettingsLineupAccount();
    }

    private void RefreshSettingsLineupAccount()
    {
        var email = _lineupSite?.Email;
        SettingsLineupAccountStatus.Text = email is null ? "登录后可以上传阵容。" : $"已登录：{email}";
        SettingsLineupAccountButton.Content = email is null ? "登录卫戍阵容库" : "管理账号";
    }

    private void OnSettingsLineupAccount(object sender, RoutedEventArgs e)
    {
        if (_lineupSite is not { } site || LineupView.IsUploading) return;
        new LineupSiteAccountWindow(site) { Owner = this }.ShowDialog();
        RefreshSettingsLineupAccount();
    }

    internal void BringForward()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal Task ImportWebsiteLineupAsync(string hash)
    {
        if (SettingsPage.Visibility == Visibility.Visible) SetSettingsVisible(false);
        MainTabs.SelectedItem = LineupTab;
        BringForward();
        return LineupView.ImportClipboardAsync(this, hash);
    }

    private string? FindLineupOperatorIcon(string name) => _lineupOperators.FirstOrDefault(item => item.Name == name)?.IconPath;
    private string? FindLineupStrategyIcon(string name) => _lineupStrategies.FirstOrDefault(strategy => strategy.Name == name)?.IconPath;

    private void OpenLineupPreview(Lineup lineup)
    {
        var preview = new ASPAssistant.App.Views.LineupPreviewView { Margin = new Thickness(18) };
        preview.ShowLineup(lineup, (equipment, name) => equipment
            ? _lineupEquipment.FirstOrDefault(item => item.Name == name)?.IconPath
            : FindLineupOperatorIcon(name), FindLineupStrategyIcon);
        var window = new Window
        {
            Title = "阵容预览 · ASP Assistant",
            Content = preview,
            Width = 860, Height = 680, MinWidth = 480, MinHeight = 420,
            Background = (System.Windows.Media.Brush)preview.FindResource("PanelBackgroundBrush"),
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Show();
    }

    private bool TryOpenLineupEditor(Lineup? lineup)
    {
        if (_lineupLibrary is null) return false;
        if (_lineupEditor is { } existing)
        {
            existing.BringForward(existing.IsComplete
                ? "当前阵容已保存。请先点击“完成”关闭窗口。"
                : "已有阵容正在编辑，请先保存或取消当前编辑。");
            return false;
        }
        var vm = new LineupEditorViewModel(_lineupOperators, _lineupEquipment, lineup, _lineupStrategies);
        vm.OperatorBrowser.SetBanChecker(BanVm.IsBanned);
        vm.OperatorBrowser.CovenantAvailability = OperatorBrowseVm.CovenantAvailability;
        void RefreshEditorBans() => Dispatcher.Invoke(() => vm.OperatorBrowser.NotifyBansChanged());
        void RefreshEditorAvailability(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(OperatorBrowseViewModel.CovenantAvailability))
                vm.OperatorBrowser.CovenantAvailability = OperatorBrowseVm.CovenantAvailability;
        }
        if (lineup is not null && !_lineupLibrary.Lineups.Any(x => x.Id == lineup.Id)) vm.IsDirty = true;
        var editor = new LineupEditorWindow(vm, async saved =>
        {
            var success = await _lineupLibrary.SaveAsync(saved);
            vm.Status = _lineupLibrary.Status;
            if (success)
            {
                MainTabs.SelectedItem = LineupTab;
                LineupView.RevealSavedLineup(saved);
            }
            return success;
        });
        BanVm.BansChanged += RefreshEditorBans;
        OperatorBrowseVm.PropertyChanged += RefreshEditorAvailability;
        editor.Closed += (_, _) =>
        {
            BanVm.BansChanged -= RefreshEditorBans;
            OperatorBrowseVm.PropertyChanged -= RefreshEditorAvailability;
            _lineupEditor = null;
        };
        _lineupEditor = editor;
        editor.Show();
        return true;
    }

    public void UpdateManualScanStatus(int pending)
        => OperatorView.UpdateManualScanStatus(pending);

    public void UpdatePreparationStatus(PreparationNotice notice)
    {
        PreparationStatusText.Text = notice.Text;
        switch (notice.Kind)
        {
            case PreparationNoticeKind.ScrollRequired:
                _preparationResultTimer.Stop();
                _waitingForScroll = true;
                ShowPreparationOverlay("请下拉补全附加盟约", notice.Text);
                break;
            case PreparationNoticeKind.Result when notice.IsNewResult || _waitingForScroll:
                _waitingForScroll = false;
                ShowPreparationOverlay("识别完成", notice.Text);
                _preparationResultTimer.Stop();
                _preparationResultTimer.Start();
                break;
            case PreparationNoticeKind.Banner when _waitingForScroll:
                ShowPreparationOverlay("继续补扫盟约", notice.Text);
                break;
            case PreparationNoticeKind.Dismiss:
                HidePreparationOverlay();
                break;
        }
    }

    private void ShowPreparationOverlay(string title, string text)
    {
        PreparationOverlayTitle.Text = title;
        PreparationOverlayText.Text = text;
        PreparationOverlay.Visibility = Visibility.Visible;
    }

    private void HidePreparationOverlay()
    {
        _preparationResultTimer.Stop();
        _waitingForScroll = false;
        PreparationOverlay.Visibility = Visibility.Collapsed;
    }

    public void StartUpdateCheck(UpdateService updateService)
    {
        _updateService = updateService;
        RefreshVersionLabel(updateService);
        _ = CheckAfterStartupAsync();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        RefreshVersionLabel(_updateService ??= new UpdateService());
        SetSettingsVisible(SettingsPage.Visibility != Visibility.Visible);
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e) => SetSettingsVisible(false);

    private void SetSettingsVisible(bool visible)
    {
        if (visible) RefreshSettingsLineupAccount();
        SettingsPage.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MainContent.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateFilterHost();
    }

    private void RefreshVersionLabel(UpdateService service)
        => SettingsVersion.Text = $"当前版本 v{service.CurrentVersion.ToString(3)}";

    private async Task CheckAfterStartupAsync()
    {
        await Task.Delay(3000);
        if (!_updateCheckAttempted) await CheckForUpdatesAsync();
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(userInitiated: true);

    private async Task CheckForUpdatesAsync(bool userInitiated = false)
    {
        if (_checkingForUpdates) return;
        var service = _updateService ??= new UpdateService();
        _updateCheckAttempted = true;
        if (service.IsDevBuild && !userInitiated)
        {
            SettingsUpdateStatus.Text = "开发构建未自动检查更新。";
            return;
        }

        _checkingForUpdates = true;
        SettingsCheckUpdateButton.IsEnabled = false;
        SettingsUpdateStatus.Text = "正在检查更新…";
        try
        {
            var info = await service.CheckForUpdateAsync(userInitiated: userInitiated);
            _pendingUpdate = info;
            UpdateBanner.Visibility = SettingsViewUpdateButton.Visibility = info is null ? Visibility.Collapsed : Visibility.Visible;
            if (info is not null) UpdateBannerText.Text = $"发现新版本 {info.TagName}";
            SettingsUpdateStatus.Text = info is null ? "未发现可安装的更新。" : $"发现新版本 {info.TagName}。";
        }
        catch (Exception ex)
        {
            SettingsUpdateStatus.Text = "检查更新失败，请检查网络后重试。";
            AppLogger.Error("Update", "Could not check for updates", ex);
        }
        finally
        {
            SettingsCheckUpdateButton.IsEnabled = true;
            _checkingForUpdates = false;
        }
    }

    private void OnShowUpdateDialog(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null || _updateService is null) return;
        var dialog = new UpdateDialog(_updateService, _pendingUpdate)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    public void UpdatePosition(RECT gameRect, bool attachInside, bool gameActuallyMoved)
    {
        _trackedGame = (gameRect, attachInside);
        _isProgrammaticChange = true;
        try
        {
            // 首次调用：清除 WPF 初始布局期间可能误设的标志
            if (!_layoutInitialized)
            {
                _layoutInitialized = true;
                _isUserSized = false;
                _isUserPositioned = false;
            }

            if (gameActuallyMoved)
                _isUserPositioned = false;

            var game = ToWindowCoordinates(gameRect);
            if (!_isUserPositioned)
            {
                Left = GetAttachedLeft(gameRect, game, attachInside);
                Top = game.Top;
            }

            if (!_isUserSized)
                Height = game.Height;
        }
        finally
        {
            _isProgrammaticChange = false;
        }
    }

    public void ClearGamePosition() => _trackedGame = null;

    private Rect ToWindowCoordinates(RECT bounds)
    {
        var rect = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target
            ? Rect.Transform(rect, target.TransformFromDevice)
            : rect;
    }

    private double GetAttachedLeft(RECT gameRect, Rect game, bool attachInside)
    {
        if (_sidebarWidth != ExpandedSidebarWidth && !attachInside)
            return game.Right - _sidebarWidth;

        var monitor = ToWindowCoordinates(User32.GetMonitorRect(gameRect.Right - 1, gameRect.Top));
        if (_sidebarWidth != ExpandedSidebarWidth)
            return monitor.Right - Width;

        var rightSpace = monitor.Right - game.Right;
        var leftSpace = game.Left - monitor.Left;
        if (rightSpace >= Width) return game.Right;
        if (leftSpace >= Width) return game.Left - Width;

        // Keep the panel visible and use the side with less overlap when neither side fits.
        return leftSpace > rightSpace ? monitor.Left : Math.Max(monitor.Left, monitor.Right - Width);
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_isProgrammaticChange && !_isRestoring)
            _isUserPositioned = true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isProgrammaticChange && !_isRestoring && e.HeightChanged)
            _isUserSized = true;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_previousWindowState != WindowState.Normal && WindowState == WindowState.Normal)
        {
            _isRestoring = true;
            LayoutUpdated += ClearRestoringFlag;
        }
        _previousWindowState = WindowState;
    }

    private void ClearRestoringFlag(object? sender, EventArgs e)
    {
        _isRestoring = false;
        LayoutUpdated -= ClearRestoringFlag;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
