using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ASPAssistant.App.Views;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
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
    public EnemyViewModel EnemyVm { get; }
    public LineupViewModel LineupVm { get; }

    public event Action? ManualScanRequested;
    public event Action<string, bool>? BanToggleRequested;

    private readonly LineupShareService _lineupShareService;
    private readonly IReadOnlyList<Operator> _allOperators;
    private readonly IReadOnlyList<Equipment> _allEquipment;
    private readonly DispatcherTimer _toastHideTimer;

    private UpdateService? _updateService;
    private SettingsManager? _settingsManager;
    private UpdateInfo? _pendingUpdate;
    private Task? _downloadTask;
    private bool _isDownloadReady;

    // 用户意图追踪
    private bool _isUserPositioned;       // 用户手动移动过窗口
    private bool _isUserSized;            // 用户手动调整过窗口高度
    private bool _isProgrammaticChange;   // 程序正在设置位置/尺寸，抑制事件响应
    private bool _isRestoring;            // 正在从最小化恢复，抑制事件响应
    private WindowState _previousWindowState;
    // WPF 初始 Show() 会触发 SizeChanged/LocationChanged，在首次 UpdatePosition 前清除误设的标志
    private bool _layoutInitialized;

    // Global hotkey (Ctrl+Shift+/) for manual screenshot scan
    private const int HotkeyIdManualScan = 0x4153; // 'AS' — arbitrary unique app-scoped ID
    private HwndSource? _hwndSource;

    // 全屏状态追踪
    private bool _wasFullscreen;
    private bool _fullscreenBannerDismissed;

    // 全屏时侧边栏的起始/结束位置（占游戏窗口高度的比例）
    private const double FullscreenTopRatio    = 0.22;  // 从顶部留出 22%
    private const double FullscreenBottomRatio = 0.68;  // 底部截止在 68% 处（留出底部 32%）

    public SidePanelWindow(
        OperatorBrowseViewModel operatorVm,
        EquipmentBrowseViewModel equipmentVm,
        TrackingViewModel trackingVm,
        GameStateViewModel gameStateVm,
        BanViewModel banVm,
        EnemyViewModel enemyVm,
        LineupViewModel lineupVm,
        LineupShareService lineupShareService,
        IReadOnlyList<Operator> allOperators,
        IReadOnlyList<Equipment> allEquipment)
    {
        OperatorBrowseVm = operatorVm;
        EquipmentBrowseVm = equipmentVm;
        TrackingVm = trackingVm;
        GameStateVm = gameStateVm;
        BanVm = banVm;
        EnemyVm = enemyVm;
        LineupVm = lineupVm;
        _lineupShareService = lineupShareService;
        _allOperators = allOperators;
        _allEquipment = allEquipment;

        InitializeComponent();

        _toastHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5),
        };
        _toastHideTimer.Tick += (_, _) => HideToast();

        LocationChanged += OnLocationChanged;
        SizeChanged     += OnSizeChanged;
        StateChanged    += OnStateChanged;

        trackingVm.GameState = gameStateVm;

        OperatorView.DataContext = operatorVm;
        EquipmentView.DataContext = equipmentVm;
        TrackingView.DataContext = trackingVm;
        EnemyView.DataContext = enemyVm;
        LineupView.DataContext = lineupVm;
        WireLineupView();

        OperatorView.IsTrackedCheck = trackingVm.IsTracked;
        EquipmentView.IsTrackedCheck = trackingVm.IsTracked;

        OperatorView.IsBannedCheck = banVm.IsBanned;

        OperatorView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        OperatorView.UntrackingRequested += name => trackingVm.RemoveTracking(name);
        EquipmentView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        EquipmentView.UntrackingRequested += name => trackingVm.RemoveTracking(name);
        TrackingView.RemoveTrackingRequested += name => trackingVm.RemoveTracking(name);
        TrackingView.ClearAllTrackingRequested += () => trackingVm.ClearAllTracking();

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource.AddHook(WndProc);
        User32.RegisterHotKey(hwnd, HotkeyIdManualScan,
            User32.MOD_CONTROL | User32.MOD_SHIFT, User32.VK_OEM_2);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HotkeyIdManualScan)
        {
            ManualScanRequested?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void UpdateManualScanStatus(int pending)
        => OperatorView.UpdateManualScanStatus(pending);

    public void SetBanProcessingMaskVisible(bool visible, string? message = null)
    {
        BanProcessingMask.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(message))
            BanProcessingMaskText.Text = message;
    }

    public void StartUpdateCheck(UpdateService updateService, SettingsManager settingsManager)
    {
        _updateService = updateService;
        _settingsManager = settingsManager;
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateService is null) return;

        // Delay slightly so the main window has time to show first
        await Task.Delay(3000);

        var info = await _updateService.CheckForUpdateAsync();
        if (info is null) return;

        // Skip if user previously asked to skip this version
        if (_settingsManager is not null)
        {
            var skipped = await _settingsManager.LoadSkippedVersionAsync();
            if (skipped == info.TagName) return;
        }

        _pendingUpdate = info;
        Dispatcher.Invoke(() =>
        {
            UpdateBannerText.Text = $"发现新版本 {info.TagName}";
            UpdateBannerProgress.Visibility = Visibility.Visible;
            UpdateBanner.Visibility = Visibility.Visible;
        });

        _downloadTask = RunBackgroundDownloadAsync(info);
    }

    private async Task RunBackgroundDownloadAsync(UpdateInfo info)
    {
        if (_updateService is null) return;

        var progress = new Progress<double>(value =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateBannerProgress.Value = value * 100;
            });
        });

        try
        {
            await _updateService.DownloadAndApplyUpdateAsync(info, progress);

            _isDownloadReady = true;
            Dispatcher.Invoke(() =>
            {
                UpdateBannerProgress.Visibility = Visibility.Collapsed;
                UpdateActionIcon.Text = "↻ ";
                UpdateActionText.Text = "立即重启";
            });
        }
        catch
        {
            Dispatcher.Invoke(() =>
            {
                UpdateBannerProgress.Visibility = Visibility.Collapsed;
                UpdateBannerText.Text = $"下载失败，点击重试";
                UpdateActionIcon.Text = "↺ ";
                UpdateActionText.Text = "重试";
            });
            _isDownloadReady = false;
        }
    }

    private void OnBannerAction(object sender, RoutedEventArgs e)
    {
        if (_isDownloadReady)
        {
            Application.Current.Shutdown();
            return;
        }

        // Download failed state — retry
        if (_downloadTask is { IsCompleted: true, IsFaulted: false } == false && _pendingUpdate is not null && _updateService is not null)
        {
            // If download task ended without success, allow retry
            if (_downloadTask is { IsCompletedSuccessfully: false } || _downloadTask is null)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateBannerText.Text = $"发现新版本 {_pendingUpdate.TagName}";
                    UpdateActionIcon.Text = "↑ ";
                    UpdateActionText.Text = "查看更新";
                    UpdateBannerProgress.Value = 0;
                    UpdateBannerProgress.Visibility = Visibility.Visible;
                });
                _downloadTask = RunBackgroundDownloadAsync(_pendingUpdate);
                return;
            }
        }

        // Download in progress — open notes dialog
        if (_pendingUpdate is null) return;
        var dialog = new UpdateDialog(_pendingUpdate)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private async void OnSkipVersion(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;
        if (_settingsManager is not null)
            await _settingsManager.SaveSkippedVersionAsync(_pendingUpdate.TagName);
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    public void UpdatePosition(RECT gameRect, bool attachInside, bool isFullscreen, bool gameActuallyMoved)
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

            // 全屏/窗口模式切换时重置用户手动标志，让位置和高度自动适配新模式
            if (isFullscreen != _wasFullscreen)
            {
                _isUserSized      = false;
                _isUserPositioned = false;
                _wasFullscreen    = isFullscreen;
                UpdateFullscreenBanner(isFullscreen);
            }

            var monitorRight = Core.Interop.User32.GetMonitorRect(gameRect.Right - 1, gameRect.Top).Right;
            var targetLeft = attachInside
                ? monitorRight - Width
                : (double)gameRect.Right;
            var targetTop = isFullscreen
                ? gameRect.Top + gameRect.Height * FullscreenTopRatio
                : (double)gameRect.Top;
            var targetHeight = isFullscreen
                ? gameRect.Height * (FullscreenBottomRatio - FullscreenTopRatio)
                : (double)gameRect.Height;

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

    private void UpdateFullscreenBanner(bool isFullscreen)
    {
        if (isFullscreen && !_fullscreenBannerDismissed)
            FullscreenBanner.Visibility = Visibility.Visible;
        else
            FullscreenBanner.Visibility = Visibility.Collapsed;
    }

    private void OnDismissFullscreenBanner(object sender, RoutedEventArgs e)
    {
        _fullscreenBannerDismissed = true;
        FullscreenBanner.Visibility = Visibility.Collapsed;
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
        User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyIdManualScan);
        Application.Current.Shutdown();
    }

    // ── Lineup feature wiring ──────────────────────────────────────────────

    private void WireLineupView()
    {
        LineupView.NewLineupRequested += () => OpenEditor(null);
        LineupView.EditLineupRequested += lineup => OpenEditor(lineup);
        LineupView.DeleteLineupRequested += async lineup =>
        {
            var ok = MessageBox.Show(
                this,
                $"确定删除阵容“{lineup.Name}”？此操作不可撤销。",
                "删除阵容",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
            try
            {
                await LineupVm.DeleteAsync(lineup);
                LineupView.RefreshAllBadges();
                ShowToast($"已删除阵容“{lineup.Name}”");
            }
            catch (Exception ex)
            {
                ShowToast($"删除失败：{ex.Message}");
            }
        };

        LineupView.ShareLineupRequested += async lineup =>
        {
            try
            {
                ShowToast("正在生成分享链接…");
                var url = await _lineupShareService.ExportAsync(lineup);
                Clipboard.SetText(url);
                ShowToast($"分享链接已复制：{url}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Lineup", $"Share failed: {ex.Message}");
                ShowToast($"分享失败：{ex.Message}");
            }
        };

        LineupView.ImportLineupRequested += async () =>
        {
            string clipboard = "";
            try { clipboard = Clipboard.GetText() ?? ""; } catch { /* ignore clipboard race */ }

            var dlg = new InputDialog(
                "导入阵容",
                "粘贴 dpaste.com 短链接 或 阵容 JSON：",
                clipboard.Trim())
            {
                Owner = this,
            };
            if (dlg.ShowDialog() != true) return;
            var input = dlg.InputText.Trim();
            if (string.IsNullOrEmpty(input)) return;

            try
            {
                ShowToast("正在导入…");
                var lineup = await _lineupShareService.ImportAsync(input);
                // Avoid id collision with existing lineups by re-id-ing on import.
                lineup.Id = Guid.NewGuid().ToString("N");
                lineup.UpdatedAt = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(lineup.Name))
                    lineup.Name = "导入的阵容";
                await LineupVm.AddOrUpdateAsync(lineup);
                LineupView.RefreshAllBadges();
                ShowToast($"已导入阵容“{lineup.Name}”");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Lineup", $"Import failed: {ex.Message}");
                ShowToast($"导入失败：{ex.Message}");
            }
        };

        LineupView.TrackLineupRequested += lineup =>
        {
            try
            {
                TrackingVm.ClearAllTracking();
                var addedOps = new HashSet<string>();
                var addedEquips = new HashSet<string>();
                foreach (var slot in lineup.Slots)
                {
                    if (!string.IsNullOrEmpty(slot.OperatorName) && addedOps.Add(slot.OperatorName))
                        TrackingVm.AddTracking(slot.OperatorName, TrackingType.Operator);
                    foreach (var eq in slot.Equipments)
                    {
                        if (!string.IsNullOrEmpty(eq) && addedEquips.Add(eq))
                            TrackingVm.AddTracking(eq, TrackingType.Equipment);
                    }
                }
                ShowToast($"已追踪阵容“{lineup.Name}”：{addedOps.Count} 干员 / {addedEquips.Count} 装备");
            }
            catch (Exception ex)
            {
                ShowToast($"追踪失败：{ex.Message}");
            }
        };
    }

    private void OpenEditor(Lineup? source)
    {
        var calc = LineupVm.Calculator;
        var editorVm = new LineupEditorViewModel(source, _allOperators, _allEquipment, calc);
        var window = new LineupEditorWindow(editorVm) { Owner = this };
        if (window.ShowDialog() != true || window.Result is null) return;

        _ = LineupVm.AddOrUpdateAsync(window.Result).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                LineupView.RefreshAllBadges();
                ShowToast(source is null ? "已创建阵容" : "已保存阵容");
            });
        });
    }

    // ── Transient toast ────────────────────────────────────────────────────

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation
        {
            From = ToastBorder.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(140),
        };
        ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _toastHideTimer.Stop();
        _toastHideTimer.Start();
    }

    private void HideToast()
    {
        _toastHideTimer.Stop();

        var fadeOut = new DoubleAnimation
        {
            From = ToastBorder.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
        };
        fadeOut.Completed += (_, _) =>
        {
            ToastBorder.Visibility = Visibility.Collapsed;
        };
        ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }
}
