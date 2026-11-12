using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
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
    private OperatorCatalog? _operatorCatalog;
    private List<Equipment> _lineupEquipment = [];
    private IReadOnlyList<Strategy> _lineupStrategies = [];
    private IReadOnlyList<string>? _lineupCovenantTags;
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
        IReadOnlyList<Strategy>? strategies = null, CovenantCatalog? covenantCatalog = null, OperatorCatalog? operatorCatalog = null)
    {
        _lineupLibrary = library;
        _lineupOperators = operators;
        _operatorCatalog = operatorCatalog;
        _lineupEquipment = equipment;
        _lineupStrategies = strategies ?? [];
        _lineupCovenantTags = covenantCatalog is null ? null :
            [.. covenantCatalog.CoreCovenants, .. covenantCatalog.AdditionalCovenants];
        if (covenantCatalog is { ActivationRules.Count: > 0 })
            LineupView.ConfigureCovenantActivation(new(covenantCatalog, operators, operatorCatalog));
        LineupView.ConfigurePortraits(FindLineupOperatorIcon, BanVm.IsBanned, FindLineupStrategyIcon);
        LineupView.DataContext = library;
        LineupView.ShareService = LineupShareService.FromCatalog(operators, equipment.Select(e => e.Name), operatorCatalog);
        LineupView.PrepareOperatorsAsync = PrepareLineupOperatorsAsync;
        LineupView.NewRequested += async () => await OpenLineupEditorAsync(null);
        LineupView.HistoryRequested += async () => await OpenGarrisonHistoryAsync();
        LineupView.EditRequested += async lineup => await OpenLineupEditorAsync(lineup);
        LineupView.PreviewRequested += async lineup =>
        {
            await PrepareLineupOperatorsAsync(lineup);
            OpenLineupPreview(lineup);
        };
        LineupView.IsVisibleChanged += async (_, args) =>
        {
            if (args.NewValue is not true || operatorCatalog is null || operatorCatalog.SelfSelected.Count > 0) return;
            await operatorCatalog.LoadForNamesAsync(library.Lineups.SelectMany(lineup =>
                lineup.Members.Select(member => member.OperatorName)));
            if (operatorCatalog.SelfSelected.Count > 0)
                LineupView.RefreshLineupCards();
        };
        LineupView.TrackRequested += lineup =>
        {
            var added = TrackingVm.TrackLineup(lineup);
            library.Status = added > 0 ? $"已加入 {added} 项干员或装备追踪。" : "这个阵容没有新的可追踪干员或装备。";
        };
    }

    private Window? _catalogDetail;
    private int _catalogDetailRequest;
    private LineupSiteClient? _lineupSite;
    private GarrisonHistoryWindow? _garrisonHistory;

    private async Task OpenGarrisonHistoryAsync()
    {
        if (_garrisonHistory is null && _operatorCatalog is { } catalog)
            await catalog.LoadSelfSelectedAsync();
        if (_garrisonHistory is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        if (!IsLoaded || LineupView.ShareService is not { } share) return;

        var converter = new GarrisonHistoryLineupConverter(
            _lineupOperators.Concat(_operatorCatalog?.SelfSelected ?? []),
            _lineupEquipment.Select(item => item.Name),
            _lineupStrategies.Select(item => item.Name));
        var window = new GarrisonHistoryWindow(converter, share, OpenLineupEditorAsync, FindLineupIcon) { Owner = this };
        window.Closed += (_, _) => _garrisonHistory = null;
        _garrisonHistory = window;
        window.Show();
    }

    internal void ConfigureCatalogDetails(CatalogDetails details, Func<Task<Dictionary<string, List<OperatorSkill>>>> loadSelfSelectedSkills)
    {
        var selfSelectedSkills = new Lazy<Task<Dictionary<string, List<OperatorSkill>>>>(loadSelfSelectedSkills);
        OperatorView.AddHandler(OperatorCard.DetailRequestedEvent, new RoutedEventHandler(async (_, e) =>
        {
            if (e.OriginalSource is not OperatorCard { DataContext: Operator op }) return;
            var request = ++_catalogDetailRequest;
            var operatorDetails = op.SelfSelected ? details with { Skills = await selfSelectedSkills.Value } : details;
            if (request == _catalogDetailRequest && IsLoaded)
                OpenCatalogDetail(new CatalogDetailWindow(op, operatorDetails, _lineupEquipment, _lineupSite));
        }));
        EquipmentView.AddHandler(EquipmentCard.DetailRequestedEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is not EquipmentCard { DataContext: Equipment equipment }) return;
            _catalogDetailRequest++;
            OpenCatalogDetail(new CatalogDetailWindow(equipment, details));
        }));
    }

    private void OpenCatalogDetail(Window detail)
    {
        _catalogDetail?.Close();
        _catalogDetail = detail;
        detail.Owner = this;
        detail.Closed += (_, _) =>
        {
            _catalogDetail = null;
            RefreshSettingsLineupAccount();
        };
        detail.Show();
    }

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
        var previousAccount = site.Email;
        new LineupSiteAccountWindow(site) { Owner = this }.ShowDialog();
        if (previousAccount != site.Email) _catalogDetail?.Close();
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

    private Task PrepareLineupOperatorsAsync(Lineup lineup) =>
        _operatorCatalog?.LoadForNamesAsync(lineup.Members.Select(member => member.OperatorName)
            .Concat(LineupMarkdown.FindOperatorReferences(lineup.Description))) ?? Task.CompletedTask;

    private string? FindLineupOperatorIcon(string name) => _operatorCatalog?.FindPortrait(name)
        ?? _lineupOperators.FirstOrDefault(item => item.Name == name)?.IconPath;
    private string? FindLineupStrategyIcon(string name) => _lineupStrategies.FirstOrDefault(strategy => strategy.Name == name)?.IconPath;

    private string? FindLineupIcon(bool equipment, string name) => equipment
        ? _lineupEquipment.FirstOrDefault(item => item.Name == name)?.IconPath
        : FindLineupOperatorIcon(name);

    private void OpenLineupPreview(Lineup lineup)
    {
        var preview = new ASPAssistant.App.Views.LineupPreviewView { Margin = new Thickness(18) };
        preview.ShowLineup(lineup, FindLineupIcon, FindLineupStrategyIcon);
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

    private async Task<bool> OpenLineupEditorAsync(Lineup? lineup)
    {
        if (_lineupLibrary is null) return false;
        if (lineup is not null) await PrepareLineupOperatorsAsync(lineup);
        if (_lineupEditor is { } existing)
        {
            existing.BringForward(existing.IsComplete
                ? "当前阵容已保存。请先点击“完成”关闭窗口。"
                : "已有阵容正在编辑，请先保存或取消当前编辑。");
            return false;
        }
        var vm = new LineupEditorViewModel(_lineupOperators, _lineupEquipment, lineup,
            _lineupStrategies, _lineupCovenantTags, _operatorCatalog);
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

    internal void ConfigureMarkerCalibration(OverlayWindow overlay)
    {
        MarkerCalibrationToggle.Checked += (_, _) => overlay.SetMarkerCalibration(true);
        MarkerCalibrationToggle.Unchecked += (_, _) => overlay.SetMarkerCalibration(false);
        ResetMarkerOffsetButton.Click += (_, _) => overlay.ResetMarkerOffset();
        overlay.MarkerOffsetSaveCompleted += saved =>
            MarkerOffsetSaveError.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetSettingsVisible(bool visible)
    {
        if (visible) RefreshSettingsLineupAccount();
        else MarkerCalibrationToggle.IsChecked = false;
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
                PositionFilterSidebar();
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
        if (WindowState == WindowState.Minimized) MarkerCalibrationToggle.IsChecked = false;
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
