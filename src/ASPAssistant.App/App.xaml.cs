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

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"启动时发生错误，请将以下信息反馈给开发者：\n\n{args.Exception}",
                "ASPAssistant 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await StartupAsync(e);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"启动时发生错误，请将以下信息反馈给开发者：\n\n{ex}",
                "ASPAssistant 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartupAsync(StartupEventArgs e)
    {

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
        var (equipment, manualJobChangeEquipments) = await dataStore.LoadEquipmentAsync();
        var trackingEntries = await _settingsManager.LoadTrackingEntriesAsync();

        // ViewModels
        var operatorVm = new OperatorBrowseViewModel();
        operatorVm.LoadOperators(operators);

        var covenantNames = operators
            .SelectMany(o => new[] { o.CoreCovenant }.Concat(o.AdditionalCovenants))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();

        var equipmentVm = new EquipmentBrowseViewModel();
        equipmentVm.LoadEquipment(equipment, covenantNames, manualJobChangeEquipments);

        var trackingVm = new TrackingViewModel();
        trackingVm.LoadEntries(trackingEntries);

        var gameStateVm = new GameStateViewModel();

        // GameState
        var gameState = new Core.GameState.GameState();

        // Game mode
        var garrisonMode = new GarrisonProtocolMode();

        // Services
        var captureService = new ScreenCaptureService();
        var cardDetector = new MaaCardDetector(dataDir);
        var ocrEngine = new MaaOcrEngine(dataDir);
        _ocrScanner = new OcrScannerService(
            captureService, garrisonMode.OcrStrategy, cardDetector, ocrEngine, gameState,
            () => trackingVm.TrackedOperators.Select(o => o.Name).ToList(),
            intervalMs: 200);

        _windowTracker = new WindowTrackerService(
            () => (int)SystemParameters.PrimaryScreenWidth);

        // Windows
        _sidePanel = new SidePanelWindow(operatorVm, equipmentVm, trackingVm, gameStateVm);
        _overlay = new OverlayWindow();

        // Wire window tracker → window positioning
        _windowTracker.GameWindowMoved += rect =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside, gameActuallyMoved: true);

                var clientRect = Core.Interop.User32.GetClientRectScreen(
                    Core.Interop.User32.FindArknightsWindow());
                if (clientRect.HasValue)
                    _overlay.UpdatePosition(clientRect.Value);
            });
        };

        _windowTracker.GameWindowPolled += rect =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside, gameActuallyMoved: false);
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

                _overlay.UpdateShopItems(state.ShopItems);
            });
        };

        // Auto-save tracking changes
        trackingVm.TrackedOperators.CollectionChanged += async (_, _) =>
        {
            try { await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList()); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save tracking: {ex.Message}"); }
        };
        trackingVm.TrackedEquipment.CollectionChanged += async (_, _) =>
        {
            try { await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList()); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save tracking: {ex.Message}"); }
        };

        // Show windows and start services
        _sidePanel.Show();
        _overlay.Show();
        _windowTracker.Start();
        _ocrScanner.Start();

        // Start background update check
        _sidePanel.StartUpdateCheck(new UpdateService());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ocrScanner?.Dispose();
        _windowTracker?.Dispose();
        base.OnExit(e);
    }
}
