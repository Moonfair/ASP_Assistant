using System.IO;
using System.Windows;
using ASPAssistant.App.Windows;
using ASPAssistant.Core;
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
            AppLogger.Error("App", "Unhandled UI exception", args.Exception);
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
            AppLogger.Error("App", "Startup failed", ex);
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

        // Initialize file logger as early as possible.
        AppLogger.Initialize(Path.Combine(settingsDir, "logs"));
        AppLogger.Info("App", $"appDir={appDir}");

        // Data layer
        var dataStore = new JsonDataStore(dataDir);
        _settingsManager = new SettingsManager(settingsDir);

        // Load data
        AppLogger.Info("App", "Loading operators...");
        var operators = await dataStore.LoadOperatorsAsync();
        var covenantCatalog = await dataStore.LoadCovenantCatalogAsync();
        AppLogger.Info("App", $"Loaded {operators.Count} operators");

        var (equipment, manualJobChangeEquipments) = await dataStore.LoadEquipmentAsync();
        AppLogger.Info("App", $"Loaded {equipment.Count} equipment items");

        var trackingEntries = await _settingsManager.LoadTrackingEntriesAsync();
        AppLogger.Info("App", $"Loaded {trackingEntries.Count} tracking entries");

        // ViewModels
        var banVm = new BanViewModel();

        var operatorVm = new OperatorBrowseViewModel();
        operatorVm.LoadOperators(operators);
        operatorVm.SetBanChecker(banVm.IsBanned);

        var covenantNames = operators
            .SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants))
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
        var ocrEngine = new MaaOcrEngine(dataDir);
        _ocrScanner = new OcrScannerService(
            captureService, garrisonMode.OcrStrategy, ocrEngine, gameState,
            () => Dispatcher.Invoke(() => trackingVm.AllEntries.Select(o => o.Name).ToList()),
            getAllOperators: () => operators.Select(o =>
                new BanCandidate(o.Name, o.CoreCovenants, o.AdditionalCovenants, [])).ToList(),
            covenantCatalog: covenantCatalog,
            intervalMs: 500,
            shopNames: operators.Select(o => o.Name).Concat(equipment.Select(e => e.Name)).ToList());

        _windowTracker = new WindowTrackerService();

        // Windows
        _sidePanel = new SidePanelWindow(operatorVm, equipmentVm, trackingVm, gameStateVm, banVm);
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

        // Wire ban detection: manual screenshot scan results → update operator ban state.
        _ocrScanner.BansDetected += names =>
        {
            Dispatcher.Invoke(() =>
            {
                banVm.SetBans(_ocrScanner.DetectedBans);
            });
        };

        // Manual screenshot scan: button in the operator browse view triggers a one-off scan.
        _sidePanel.ManualScanRequested += () => _ocrScanner!.EnqueueManualScan();
        _sidePanel.ClearBansRequested += () => _ocrScanner!.ClearBans();
        _ocrScanner.PreparationStatusChanged += notice =>
            Dispatcher.InvokeAsync(() => _sidePanel.UpdatePreparationStatus(notice));

        // Manual ban/unban from operator card button.
        _sidePanel.BanToggleRequested += (name, banned) =>
        {
            banVm.ToggleBan(name);
            _ocrScanner!.SetManualBan(name, banned);
        };
        _ocrScanner.ManualScanQueueChanged += count =>
            Dispatcher.Invoke(() => _sidePanel.UpdateManualScanStatus(count));


        // Auto-save tracking changes
        trackingVm.TrackedOperators.CollectionChanged += async (_, _) =>
        {
            try { await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList()); }
            catch (Exception ex) { AppLogger.Error("Settings", "Failed to save tracking", ex); }
        };
        trackingVm.TrackedEquipment.CollectionChanged += async (_, _) =>
        {
            try { await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList()); }
            catch (Exception ex) { AppLogger.Error("Settings", "Failed to save tracking", ex); }
        };

        // Show windows and start services
        _sidePanel.Show();
        _overlay.Show();
        _windowTracker.Start();
        _ocrScanner.Start();
        AppLogger.Info("App", "Startup complete");

        // Start background update check
        _sidePanel.StartUpdateCheck(new UpdateService());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("App", "Shutting down");
        _ocrScanner?.Dispose();
        _windowTracker?.Dispose();
        base.OnExit(e);
    }
}
