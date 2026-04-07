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
        // Catches exceptions before the WPF Dispatcher is running (e.g. DllNotFoundException
        // from MaaFramework native libs if VC++ Runtime is missing, or single-file extraction failures).
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            AppLogger.Error("App", "Fatal unhandled exception",
                ex ?? new Exception(args.ExceptionObject?.ToString()));

            var isDllMissing = ex is DllNotFoundException
                || (ex?.InnerException is DllNotFoundException)
                || args.ExceptionObject?.ToString()?.Contains("DllNotFoundException") == true;

            var message = isDllMissing
                ? "缺少必要的运行库，程序无法启动。\n\n" +
                  "请先安装 Microsoft Visual C++ Redistributable（x64），然后重新运行：\n" +
                  "https://aka.ms/vs/17/release/vc_redist.x64.exe\n\n" +
                  $"技术详情：\n{ex?.GetType().Name}: {ex?.Message}"
                : $"程序启动时发生严重错误，请将以下信息反馈给开发者：\n\n{args.ExceptionObject}";

            MessageBox.Show(
                message,
                "ASPAssistant 崩溃",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        // Prevents background Task exceptions from being silently swallowed.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Warn("App", $"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

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

            // DllNotFoundException from MaaFramework almost always means the Visual C++
            // Redistributable (vcruntime140_1.dll) is not installed on the user's machine.
            var isDllMissing = ex is DllNotFoundException
                || (ex.InnerException is DllNotFoundException)
                || ex.ToString().Contains("DllNotFoundException");

            var message = isDllMissing
                ? "缺少必要的运行库，程序无法启动。\n\n" +
                  "请先安装 Microsoft Visual C++ Redistributable（x64），然后重新运行：\n" +
                  "https://aka.ms/vs/17/release/vc_redist.x64.exe\n\n" +
                  $"技术详情：\n{ex.GetType().Name}: {ex.Message}"
                : $"启动时发生错误，请将以下信息反馈给开发者：\n\n{ex}";

            MessageBox.Show(
                message,
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
        AppLogger.Initialize(Path.Combine(appDir, "logs"));
        AppLogger.Info("App", $"appDir={appDir}");

        // Data layer
        var dataStore = new JsonDataStore(dataDir);
        _settingsManager = new SettingsManager(settingsDir);

        // Load data
        AppLogger.Info("App", "Loading operators...");
        var operators = await dataStore.LoadOperatorsAsync();
        AppLogger.Info("App", $"Loaded {operators.Count} operators");

        var (equipment, manualJobChangeEquipments) = await dataStore.LoadEquipmentAsync();
        AppLogger.Info("App", $"Loaded {equipment.Count} equipment items");

        var skinAvatarMap = await dataStore.LoadSkinAvatarMapAsync();
        AppLogger.Info("App", $"Loaded {skinAvatarMap.Count} skin avatar entries");

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
        var cardDetector = new MaaCardDetector(dataDir);
        var ocrEngine = new MaaOcrEngine(dataDir);
        var banIconMatcher = new MaaBanIconMatcher(dataDir);
        _ocrScanner = new OcrScannerService(
            captureService, garrisonMode.OcrStrategy, cardDetector, ocrEngine, gameState,
            () => trackingVm.TrackedOperators.Select(o => o.Name).ToList(),
            getAllOperators: () =>
            {
                var covenantLookup = operators.ToDictionary(
                    o => o.Name,
                    o => ((IReadOnlyList<string>)o.CoreCovenants, (IReadOnlyList<string>)o.AdditionalCovenants));
                return skinAvatarMap
                    .GroupBy(kvp => kvp.Value)
                    .Select(g =>
                    {
                        covenantLookup.TryGetValue(g.Key, out var cv);
                        return (g.Key,
                                cv.Item1 ?? (IReadOnlyList<string>)[],
                                cv.Item2 ?? (IReadOnlyList<string>)[],
                                (IReadOnlyList<string>)g.Select(kvp => $"skin_avatars/{kvp.Key}").ToList());
                    })
                    .ToList();
            },
            iconMatcher: banIconMatcher,
            intervalMs: 1000);

        _windowTracker = new WindowTrackerService();

        // Windows
        _sidePanel = new SidePanelWindow(operatorVm, equipmentVm, trackingVm, gameStateVm, banVm);
        _overlay = new OverlayWindow();

        // Wire window tracker → window positioning
        _windowTracker.GameWindowMoved += rect =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside,
                    _windowTracker.IsFullscreen, gameActuallyMoved: true);

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
                _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside,
                    _windowTracker.IsFullscreen, gameActuallyMoved: false);
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
                banVm.SetBans(names);
            });
        };

        // Ban screen overlay guidance: show once per entry, hide on exit.
        // _banInstructionShown prevents re-showing after the user dismisses by scrolling
        // while still remaining on the ban screen.
        bool banInstructionShown = false;
        _ocrScanner.BanScreenDetected += () => Dispatcher.Invoke(() =>
        {
            if (!banInstructionShown)
            {
                _overlay!.ShowBanInstructionOverlay();
                banInstructionShown = true;
            }
        });
        _ocrScanner.BanScreenLost += () => Dispatcher.Invoke(() =>
        {
            _overlay!.HideBanInstructionOverlay();
            banInstructionShown = false;
        });

        // Manual screenshot scan: button in the operator browse view triggers a one-off scan.
        _sidePanel.ManualScanRequested += () => _ocrScanner!.EnqueueManualScan();

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
        AppLogger.Info("App", "Startup complete");

        // Start background update check
        _sidePanel.StartUpdateCheck(new UpdateService(), _settingsManager!);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("App", "Shutting down");
        _ocrScanner?.Dispose();
        _windowTracker?.Dispose();
        base.OnExit(e);
    }
}
