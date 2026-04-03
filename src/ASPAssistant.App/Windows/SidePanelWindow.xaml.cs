using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow : Window
{
    public OperatorBrowseViewModel OperatorBrowseVm { get; }
    public EquipmentBrowseViewModel EquipmentBrowseVm { get; }
    public TrackingViewModel TrackingVm { get; }
    public GameStateViewModel GameStateVm { get; }
    public BanViewModel BanVm { get; }

    public event Action? ManualScanRequested;
    public event Action<string, bool>? BanToggleRequested;

    private UpdateService? _updateService;
    private UpdateInfo? _pendingUpdate;

    // 用户意图追踪
    private bool _isUserPositioned;       // 用户手动移动过窗口
    private bool _isUserSized;            // 用户手动调整过窗口高度
    private bool _isProgrammaticChange;   // 程序正在设置位置/尺寸，抑制事件响应
    private bool _isRestoring;            // 正在从最小化恢复，抑制事件响应
    private WindowState _previousWindowState;
    // WPF 初始 Show() 会触发 SizeChanged/LocationChanged，在首次 UpdatePosition 前清除误设的标志
    private bool _layoutInitialized;

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

        LocationChanged += OnLocationChanged;
        SizeChanged     += OnSizeChanged;
        StateChanged    += OnStateChanged;

        trackingVm.GameState = gameStateVm;

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
        TrackingView.RemoveTrackingRequested += name => trackingVm.RemoveTracking(name);

        OperatorView.ClearBansRequested += () => banVm.ClearBans();
        OperatorView.ManualScanRequested += () => ManualScanRequested?.Invoke();
        OperatorView.BanToggleRequested += (name, banned) => BanToggleRequested?.Invoke(name, banned);

        banVm.BansChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                OperatorView.RefreshBannedStates();
                operatorVm.NotifyBansChanged();
            });
        };

        trackingVm.TrackedOperators.CollectionChanged += (_, _) =>
        {
            OperatorView.RefreshTrackingStates();
            EquipmentView.RefreshTrackingStates();
        };
        trackingVm.TrackedEquipment.CollectionChanged += (_, _) =>
        {
            OperatorView.RefreshTrackingStates();
            EquipmentView.RefreshTrackingStates();
        };
    }

    public void UpdateManualScanStatus(int pending)
        => OperatorView.UpdateManualScanStatus(pending);

    public void StartUpdateCheck(UpdateService updateService)
    {
        _updateService = updateService;
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateService is null) return;

        // Delay slightly so the main window has time to show first
        await Task.Delay(3000);

        var info = await _updateService.CheckForUpdateAsync();
        if (info is null) return;

        _pendingUpdate = info;
        Dispatcher.Invoke(() =>
        {
            UpdateBannerText.Text = $"发现新版本 {info.TagName}";
            UpdateBanner.Visibility = Visibility.Visible;
        });
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

            var monitorRight = Core.Interop.User32.GetMonitorRect(gameRect.Right - 1, gameRect.Top).Right;
            var targetLeft = attachInside
                ? monitorRight - Width
                : (double)gameRect.Right;
            var targetTop  = (double)gameRect.Top;
            var targetHeight = (double)gameRect.Height;

            if (!_isUserPositioned)
            {
                Left = targetLeft;
                Top  = targetTop;
            }

            if (!_isUserSized)
                Height = targetHeight;
        }
        finally
        {
            _isProgrammaticChange = false;
        }
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
        Application.Current.Shutdown();
    }
}
