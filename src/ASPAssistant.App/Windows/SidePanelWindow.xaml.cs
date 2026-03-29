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

    private UpdateService? _updateService;
    private UpdateInfo? _pendingUpdate;

    public SidePanelWindow(
        OperatorBrowseViewModel operatorVm,
        EquipmentBrowseViewModel equipmentVm,
        TrackingViewModel trackingVm,
        GameStateViewModel gameStateVm)
    {
        OperatorBrowseVm = operatorVm;
        EquipmentBrowseVm = equipmentVm;
        TrackingVm = trackingVm;
        GameStateVm = gameStateVm;

        InitializeComponent();

        trackingVm.GameState = gameStateVm;

        OperatorView.DataContext = operatorVm;
        EquipmentView.DataContext = equipmentVm;
        TrackingView.DataContext = trackingVm;

        OperatorView.IsTrackedCheck = trackingVm.IsTracked;
        EquipmentView.IsTrackedCheck = trackingVm.IsTracked;

        OperatorView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        EquipmentView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        TrackingView.RemoveTrackingRequested += name => trackingVm.RemoveTracking(name);
    }

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

    public void UpdatePosition(RECT gameRect, bool attachInside)
    {
        if (attachInside)
        {
            Left = gameRect.Right - Width;
            Top = gameRect.Top;
            Height = gameRect.Height;
        }
        else
        {
            Left = gameRect.Right;
            Top = gameRect.Top;
            Height = gameRect.Height;
        }
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
