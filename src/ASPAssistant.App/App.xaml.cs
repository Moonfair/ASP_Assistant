using System.IO;
using System.Security;
using ASPAssistant.App.Services;
using System.Windows;
using ASPAssistant.App.Windows;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
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
    private LineupActivation? _activation;
    private LineupSiteClient? _lineupSite;
    private readonly TaskCompletionSource<SidePanelWindow> _readyForActivation = new();
    private Task _trackingSave = Task.CompletedTask;

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
            var hash = LineupProtocol.ParseImport(e.Args);
            if (e.Args.FirstOrDefault() == "--lineup-uri" && hash is null)
            {
                Shutdown(1);
                return;
            }

            _activation = new LineupActivation();
            if (!_activation.IsPrimary)
            {
                await _activation.ForwardAsync(hash);
                Shutdown();
                return;
            }

            _ = ListenForActivationAsync();
            await StartupAsync();
            if (hash is not null) await ActivateLineupAsync(hash);
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

    private async Task ListenForActivationAsync()
    {
        try { await _activation!.ListenAsync(ActivateLineupAsync); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLogger.Error("Activation", "Could not listen for app activations", ex); }
    }

    private async Task ActivateLineupAsync(string? hash)
    {
        var panel = await _readyForActivation.Task;
        if (hash is null) panel.BringForward();
        else await panel.ImportWebsiteLineupAsync(hash);
    }

    private async Task StartupAsync()
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
        try { LineupProtocol.Register(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppLogger.Warn("Activation", $"Could not register aspassistant protocol: {ex.Message}");
        }

        // Data layer
        var dataStore = new JsonDataStore(dataDir);
        _settingsManager = new SettingsManager(settingsDir);

        // Load data
        AppLogger.Info("App", "Loading operators...");
        var operators = await dataStore.LoadOperatorsAsync();
        var operatorCatalog = new OperatorCatalog(operators, dataStore.LoadSelfSelectedOperatorsAsync);
        var shopOperators = operators.Where(op => !op.StrategyOnly).ToList();
        var covenantCatalog = await dataStore.LoadCovenantCatalogAsync();
        var strategies = await dataStore.LoadStrategiesAsync();
        AppLogger.Info("App", $"Loaded {operators.Count} operators");

        var (equipment, manualJobChangeEquipments) = await dataStore.LoadEquipmentAsync();
        AppLogger.Info("App", $"Loaded {equipment.Count} equipment items");

        var trackingEntries = await _settingsManager.LoadTrackingEntriesAsync();
        AppLogger.Info("App", $"Loaded {trackingEntries.Count} tracking entries");

        // ViewModels
        var banVm = new BanViewModel();

        var operatorVm = new OperatorBrowseViewModel(operatorCatalog);
        operatorVm.LoadOperators(operators);
        operatorVm.SetBanChecker(banVm.IsBanned);

        var covenantNames = operators
            .SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();

        var equipmentVm = new EquipmentBrowseViewModel();
        equipmentVm.LoadEquipment(equipment, covenantNames, manualJobChangeEquipments);

        var trackingVm = new TrackingViewModel(operators, equipment, operatorCatalog);
        trackingVm.LoadEntries(trackingEntries);

        var gameStateVm = new GameStateViewModel();
        var lineupVm = new LineupLibraryViewModel(new LineupStore(settingsDir));
        await lineupVm.LoadAsync();
        _lineupSite = new LineupSiteClient(settingsDir);
        await _lineupSite.RestoreSessionAsync();

        // GameState
        var gameState = new Core.GameState.GameState();

        // Game mode
        var garrisonMode = new GarrisonProtocolMode();

        // Services
        var captureService = new ScreenCaptureService();
        var ocrEngine = new MaaOcrEngine(dataDir);
        _ocrScanner = new OcrScannerService(
            captureService, _gameTarget, garrisonMode.OcrStrategy, ocrEngine, gameState,
            () => Dispatcher.Invoke(() => trackingVm.AllEntries.Select(o => o.Name).ToList()),
            getAllOperators: () => shopOperators.Select(o =>
                new BanCandidate(o.Name, o.CoreCovenants, o.AdditionalCovenants, [])).ToList(),
            covenantCatalog: covenantCatalog,
            intervalMs: 500,
            shopNames: shopOperators.Select(o => o.Name).Concat(equipment.Select(e => e.Name)).ToList(),
            hudRecognizer: new GarrisonHudRecognizer(ocrEngine));

        _windowTracker = new WindowTrackerService(_gameTarget);

        // Windows
        _sidePanel = new SidePanelWindow(operatorVm, equipmentVm, trackingVm, gameStateVm, banVm);
        _sidePanel.UseSideFilters = _settingsManager.LoadSideFilterLayout();
        _sidePanel.FilterLayoutChanged += side =>
        {
            try { _settingsManager.SaveSideFilterLayout(side); }
            catch (Exception ex) { AppLogger.Error("Settings", "Could not save filter layout", ex); }
        };
        _sidePanel.ConfigureLineups(lineupVm, operators, equipment, strategies, covenantCatalog, operatorCatalog);
        _sidePanel.ConfigureLineupSite(_lineupSite);
        var catalogDetails = await dataStore.LoadCatalogDetailsAsync();
        _sidePanel.ConfigureCatalogDetails(catalogDetails, () => dataStore.LoadSelfSelectedSkillsAsync(catalogDetails.Season));
        _overlay = new OverlayWindow(_settingsManager);
        _sidePanel.ConfigureMarkerCalibration(_overlay);
        ConfigureGameSource();
        bool hasCurrentGameState = false;
        _gameTarget.Changed += () =>
        {
            hasCurrentGameState = false;
            _sidePanel.ClearGamePosition();
            _overlay.SetTarget(_gameTarget.Current);
            _overlay.Hide();
            gameStateVm.UpdateFromGameState(new Core.GameState.GameState());
            banVm.SetBans([]);
            operatorVm.CovenantAvailability = null;
            _sidePanel.UpdatePreparationStatus(new PreparationNotice("自动 BP 检测已开启，等待准备界面",
                PreparationNoticeKind.Dismiss));
        };

        void PositionGameWindows(Core.Interop.RECT rect, bool moved)
        {
            Dispatcher.Invoke(() =>
            {
                var target = _windowTracker.CurrentTarget;
                if (target == null || !ReferenceEquals(target, _gameTarget.Current)) return;
                _sidePanel.UpdatePosition(rect, gameActuallyMoved: moved);
                _overlay.SetTarget(target);
                var viewport = captureService.GetViewport(target);
                var calibratingFromPanel = _overlay.IsCalibratingMarkers && _sidePanel.IsActive;
                if (viewport == null || (target.Kind != GameTargetKind.Windows &&
                        !_windowTracker.IsGameFocused && !calibratingFromPanel))
                    _overlay.Hide();
                else
                    _overlay.UpdatePosition(viewport.Value);
            });
        }
        _windowTracker.GameWindowMoved += rect => PositionGameWindows(rect, moved: true);
        _windowTracker.GameWindowPolled += rect => PositionGameWindows(rect, moved: false);

        _windowTracker.GameWindowLost += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.ClearGamePosition();
                _overlay.Hide();
            });
        };

        void RefreshShopTracking(Core.GameState.GameState state, List<TrackingEntry> entries)
        {
            var shopItems = state.ShopItems;
            Core.GameState.GameStateUpdater.UpdateShopTracking(shopItems, entries);
            gameStateVm.UpdateFromGameState(state);
            _overlay.UpdateShopItems(shopItems);
        }

        // Wire OCR scanner → GameState → overlay markers
        _ocrScanner.GameStateUpdated += (target, state) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(target, _gameTarget.Current)) return;
                _overlay.SetTarget(target);
                hasCurrentGameState = true;
                state.CovenantCounts = Core.GameState.GameStateUpdater.ComputeCovenantCounts(
                    [.. state.FieldOperators, .. state.BenchOperators], [.. operators, .. operatorCatalog.SelfSelected]);
                RefreshShopTracking(state, trackingVm.AllEntries.ToList());
            });
        };

        // Wire ban detection: manual screenshot scan results → update operator ban state.
        _ocrScanner.BansDetected += (target, names) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (ReferenceEquals(target, _gameTarget.Current)) banVm.SetBans(names);
            });
        };
        _ocrScanner.CovenantAvailabilityChanged += (target, availability) => Dispatcher.Invoke(() =>
        {
            if (ReferenceEquals(target, _gameTarget.Current)) operatorVm.CovenantAvailability = availability;
        });

        // Manual screenshot scan: button in the operator browse view triggers a one-off scan.
        _sidePanel.ManualScanRequested += () => _ocrScanner!.EnqueueManualScan();
        _sidePanel.ClearBansRequested += () => _ocrScanner!.ClearBans();
        _ocrScanner.PreparationStatusChanged += (target, notice) => Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(target, _gameTarget.Current)) _sidePanel.UpdatePreparationStatus(notice);
        });

        // Manual ban/unban from operator card button.
        _sidePanel.BanToggleRequested += (name, banned) =>
        {
            banVm.ToggleBan(name);
            _ocrScanner!.SetManualBan(name, banned);
        };
        _ocrScanner.ManualScanQueueChanged += count =>
            Dispatcher.Invoke(() => _sidePanel.UpdateManualScanStatus(count));


        // Save tracking changes and refresh markers even if the shop stays the same.
        trackingVm.TrackingChanged += () =>
        {
            var entries = trackingVm.AllEntries.ToList();
            _trackingSave = SaveTrackingAsync(_trackingSave, entries);
            if (hasCurrentGameState) RefreshShopTracking(gameState, entries);
        };

        // Show windows and start services
        _sidePanel.Show();
        _windowTracker.Start();
        _ocrScanner.Start();
        _readyForActivation.SetResult(_sidePanel);
        AppLogger.Info("App", "Startup complete");

        // Start background update check
        _sidePanel.StartUpdateCheck(new UpdateService());
    }

    private async Task SaveTrackingAsync(Task previousSave, List<TrackingEntry> entries)
    {
        try
        {
            await previousSave.ConfigureAwait(false);
            await _settingsManager!.SaveTrackingEntriesAsync(entries).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Error("Settings", "Failed to save tracking", ex); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("App", "Shutting down");
        _readyForActivation.TrySetCanceled();
        _activation?.Dispose();
        _lineupSite?.Dispose();
        StopGameSource();
        _ocrScanner?.Dispose();
        _windowTracker?.Dispose();
        _trackingSave.GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
