using System.IO;
using System.Windows;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App;

public partial class App : Application
{
    private WindowTrackerService? _windowTracker;
    private OcrScannerService? _ocrScanner;
    private SidePanelWindow? _sidePanel;
    private OverlayWindow? _overlay;
    private SettingsManager? _settingsManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Paths
        var appDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(appDir, "data");
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ASPAssistant");

        // Data layer
        var dataStore = new JsonDataStore(dataDir);
        _settingsManager = new SettingsManager(settingsDir);

        // Load data
        var operators = await dataStore.LoadOperatorsAsync();
        var equipment = await dataStore.LoadEquipmentAsync();
        var trackingEntries = await _settingsManager.LoadTrackingEntriesAsync();

        // ViewModels
        var operatorVm = new OperatorBrowseViewModel();
        operatorVm.LoadOperators(operators);

        var equipmentVm = new EquipmentBrowseViewModel();
        equipmentVm.LoadEquipment(equipment);

        var trackingVm = new TrackingViewModel();
        trackingVm.LoadEntries(trackingEntries);

        var gameStateVm = new GameStateViewModel();

        // GameState
        var gameState = new Core.GameState.GameState();

        // Game mode
        var garrisonMode = new GarrisonProtocolMode();

        // Services
        var captureService = new ScreenCaptureService();
        _ocrScanner = new OcrScannerService(
            captureService, garrisonMode.OcrStrategy, gameState,
            _settingsManager.OcrScanIntervalSeconds);

        _windowTracker = new WindowTrackerService();

        // Windows
        _sidePanel = new SidePanelWindow(operatorVm, equipmentVm, trackingVm, gameStateVm);
        _overlay = new OverlayWindow();

        // Wire window tracker → window positioning
        _windowTracker.GameWindowMoved += rect =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside);

                var clientRect = Core.Interop.User32.GetClientRectScreen(
                    Core.Interop.User32.FindArknightsWindow());
                if (clientRect.HasValue)
                    _overlay.UpdatePosition(clientRect.Value);
            });
        };

        _windowTracker.GameWindowLost += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _overlay.Hide();
            });
        };

        // Wire OCR scanner → GameState → overlay markers
        _ocrScanner.GameStateUpdated += state =>
        {
            Dispatcher.Invoke(() =>
            {
                Core.GameState.GameStateUpdater.UpdateShopTracking(
                    state.ShopItems, trackingVm.AllEntries.ToList());

                state.CovenantCounts = Core.GameState.GameStateUpdater.ComputeCovenantCounts(
                    [.. state.FieldOperators, .. state.BenchOperators], operators);

                trackingVm.CovenantCounts = state.CovenantCounts;
                gameStateVm.UpdateFromGameState(state);

                var trackedItems = state.ShopItems.Where(s => s.IsTracked).ToList();
                var clientRect = Core.Interop.User32.GetClientRectScreen(
                    Core.Interop.User32.FindArknightsWindow());
                if (clientRect.HasValue && trackedItems.Count > 0)
                {
                    _overlay.Show();
                    _overlay.UpdateMarkers(state.ShopItems, clientRect.Value);
                }
                else
                {
                    _overlay.OverlayCanvas.Children.Clear();
                }
            });
        };

        // Auto-save tracking changes
        trackingVm.TrackedOperators.CollectionChanged += async (_, _) =>
            await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList());
        trackingVm.TrackedEquipment.CollectionChanged += async (_, _) =>
            await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList());

        // Show windows and start services
        _sidePanel.Show();
        _overlay.Show();
        _windowTracker.Start();
        _ocrScanner.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ocrScanner?.Dispose();
        _windowTracker?.Dispose();
        base.OnExit(e);
    }
}
