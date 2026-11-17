using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using Avalonia.Threading;

namespace ASPAssistant.Mac;

public sealed class MacWorkspace : IDisposable
{
    private readonly SettingsManager _settings;
    private readonly OcrScannerService? _scanner;
    private readonly ScreenCaptureService? _capture;
    private readonly WindowTrackerService _windowTracker;
    private readonly Timer _autoTargetTimer;
    private readonly object _sourceGate = new();
    private int _autoTargetPolling;
    private readonly SemaphoreSlim _trackingSaveGate = new(1, 1);

    public string DataDirectory { get; }
    public string SettingsDirectory { get; }
    public IReadOnlyList<Operator> Operators { get; }
    public IReadOnlyList<Equipment> Equipment { get; }
    public IReadOnlyList<Strategy> Strategies { get; }
    public OperatorCatalog OperatorCatalog { get; }
    public CovenantCatalog CovenantCatalog { get; }
    public CatalogDetails CatalogDetails { get; }
    public OperatorBrowseViewModel OperatorBrowse { get; }
    public EquipmentBrowseViewModel EquipmentBrowse { get; }
    public TrackingViewModel Tracking { get; }
    public BanViewModel Bans { get; }
    public LineupLibraryViewModel Lineups { get; }
    internal LineupSiteClient Site { get; }
    public GameTargetSelection GameSource { get; }
    public WindowTrackerService WindowTracker => _windowTracker;
    public ScreenCaptureService? Capture => _capture;
    public Core.GameState.GameState CurrentGameState { get; private set; } = new();
    public string ScannerStatus { get; private set; } = "";
    public event Action? GameStateChanged;
    public event Action? ScannerStatusChanged;

    private MacWorkspace(string dataDirectory, string settingsDirectory,
        SettingsManager settings, IReadOnlyList<Operator> operators, IReadOnlyList<Equipment> equipment,
        IReadOnlyList<Strategy> strategies, OperatorCatalog catalog, CovenantCatalog covenants,
        CatalogDetails details, OperatorBrowseViewModel operatorBrowse,
        EquipmentBrowseViewModel equipmentBrowse, TrackingViewModel tracking, BanViewModel bans,
        LineupLibraryViewModel lineups, GameTargetSelection selection,
        ScreenCaptureService? capture, OcrScannerService? scanner)
    {
        DataDirectory = dataDirectory;
        SettingsDirectory = settingsDirectory;
        _settings = settings;
        Operators = operators;
        Equipment = equipment;
        Strategies = strategies;
        OperatorCatalog = catalog;
        CovenantCatalog = covenants;
        CatalogDetails = details;
        OperatorBrowse = operatorBrowse;
        EquipmentBrowse = equipmentBrowse;
        Tracking = tracking;
        Bans = bans;
        Lineups = lineups;
        Site = new LineupSiteClient(settingsDirectory);
        GameSource = selection;
        _scanner = scanner;
        _capture = capture;
        _windowTracker = new WindowTrackerService(selection, 250);
        _scanner?.Start();
        _windowTracker.Start();
        _autoTargetTimer = new Timer(_ => PollAutomaticTarget(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static async Task<MacWorkspace> LoadAsync(string? dataDirectory = null,
        string? settingsDirectory = null, bool initializeScanner = true)
    {
        dataDirectory ??= Path.Combine(AppContext.BaseDirectory, "data");
        settingsDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "ASPAssistant");
        AppLogger.Initialize(Path.Combine(settingsDirectory, "logs"));

        var data = new JsonDataStore(dataDirectory);
        var operators = await data.LoadOperatorsAsync();
        if (operators.Count == 0) throw new FileNotFoundException("未找到干员数据 operators.json", dataDirectory);
        var catalog = new OperatorCatalog(operators, data.LoadSelfSelectedOperatorsAsync);
        var (equipment, jobChanges) = await data.LoadEquipmentAsync();
        var covenants = await data.LoadCovenantCatalogAsync();
        var strategies = await data.LoadStrategiesAsync();
        var details = await data.LoadCatalogDetailsAsync();
        var settings = new SettingsManager(settingsDirectory);

        var bans = new BanViewModel();
        var operatorBrowse = new OperatorBrowseViewModel(catalog);
        operatorBrowse.LoadOperators(operators);
        operatorBrowse.SetBanChecker(bans.IsBanned);
        bans.BansChanged += operatorBrowse.NotifyBansChanged;
        var equipmentBrowse = new EquipmentBrowseViewModel();
        equipmentBrowse.LoadEquipment(equipment,
            operators.SelectMany(op => op.CoreCovenants.Concat(op.AdditionalCovenants)).Distinct(), jobChanges);
        var tracking = new TrackingViewModel(operators, equipment, catalog);
        tracking.LoadEntries(await settings.LoadTrackingEntriesAsync());
        var lineups = new LineupLibraryViewModel(new LineupStore(settingsDirectory));
        await lineups.LoadAsync();

        var selection = new GameTargetSelection();
        var savedSource = settings.LoadGameTarget();
        if (savedSource.Id == "adb" && savedSource.AdbPath is { } savedAdb &&
            File.Exists(savedAdb) && savedSource.Serial is { } savedSerial &&
            GameTargetSelection.IsLocalAdbSerial(savedSerial))
            selection.Select(savedSource, GameTargetSelection.FindManualTarget(savedSource,
                GameTargetSelection.ListWindows()));
        else if (savedSource.Id == GameTarget.WindowsId)
            selection.Select(savedSource, GameTargetSelection.FindWindowsClient());
        OcrScannerService? scanner = null;
        ScreenCaptureService? capture = null;
        string status = "自动识别未启动";
        if (initializeScanner)
        {
            try
            {
                var ocr = new MaaOcrEngine(dataDirectory);
                capture = new ScreenCaptureService();
                scanner = new OcrScannerService(capture, selection,
                    new GarrisonOcrStrategy(), ocr, new Core.GameState.GameState(),
                    () => tracking.AllEntries.Select(entry => entry.Name).ToArray(),
                    () => operators.Where(op => !op.StrategyOnly).Select(op =>
                        new BanCandidate(op.Name, op.CoreCovenants, op.AdditionalCovenants, [])).ToArray(),
                    covenants, intervalMs: 600,
                    shopNames: operators.Where(op => !op.StrategyOnly).Select(op => op.Name)
                        .Concat(equipment.Select(item => item.Name)).ToArray(),
                    hudRecognizer: new GarrisonHudRecognizer(ocr));
                status = selection.Current is { } restored
                    ? "正在识别 " + restored.Name : "等待选择游戏来源";
            }
            catch (Exception ex)
            {
                AppLogger.Error("MacApp", "OCR initialization failed", ex);
                status = "OCR 无法启动：" + ex.Message;
            }
        }

        var workspace = new MacWorkspace(dataDirectory, settingsDirectory, settings,
            operators, equipment, strategies, catalog, covenants, details,
            operatorBrowse, equipmentBrowse, tracking, bans, lineups, selection, capture, scanner)
        {
            ScannerStatus = status
        };
        // The scanner and window tracker must use one shared target selection.
        workspace.GameSource.Changed += () => workspace.SetStatus(workspace.GameSource.Current is null
            ? "等待选择游戏来源" : "正在识别 " + workspace.GameSource.Current.Name);
        if (scanner != null) workspace.AttachScanner(scanner);
        workspace._autoTargetTimer.Change(0, 1000);
        await workspace.Site.RestoreSessionAsync();
        return workspace;
    }

    private void AttachScanner(OcrScannerService scanner)
    {
        scanner.GameStateUpdated += (target, state) =>
        {
            if (!ReferenceEquals(GameSource.Current, target)) return;
            Dispatcher.UIThread.Post(() =>
            {
                CurrentGameState = state;
                CurrentGameState.CovenantCounts = Core.GameState.GameStateUpdater.ComputeCovenantCounts(
                    [.. state.FieldOperators, .. state.BenchOperators], Operators.ToList());
                GameStateChanged?.Invoke();
            });
        };
        scanner.BansDetected += (target, names) =>
        {
            if (ReferenceEquals(GameSource.Current, target)) Dispatcher.UIThread.Post(() => Bans.SetBans(names));
        };
        scanner.CovenantAvailabilityChanged += (target, availability) =>
        {
            if (ReferenceEquals(GameSource.Current, target)) Dispatcher.UIThread.Post(() =>
                OperatorBrowse.CovenantAvailability = availability);
        };
        scanner.PreparationStatusChanged += (target, notice) =>
        {
            if (ReferenceEquals(GameSource.Current, target)) Dispatcher.UIThread.Post(() => SetStatus(notice.Text));
        };
    }

    private void SetStatus(string text)
    {
        ScannerStatus = text;
        ScannerStatusChanged?.Invoke();
    }

    private void PollAutomaticTarget()
    {
        if (GameSource.Preference.Id != GameTarget.AutomaticId ||
            Interlocked.Exchange(ref _autoTargetPolling, 1) != 0) return;
        try
        {
            var found = GameTargetSelection.FindWindowsClient();
            lock (_sourceGate)
                if (GameSource.Preference.Id == GameTarget.AutomaticId) GameSource.SetCurrent(found);
        }
        catch (Exception ex) { AppLogger.Warn("MacSource", "Automatic source lookup failed: " + ex.Message); }
        finally { Volatile.Write(ref _autoTargetPolling, 0); }
    }

    public void SelectAutomaticSource()
    {
        var preference = new GameTargetPreference();
        var target = GameTargetSelection.FindWindowsClient();
        lock (_sourceGate)
        {
            GameSource.Select(preference, target);
            _settings.SaveGameTarget(preference);
        }
    }

    public void SelectAdbSource(HostWindow window, string adbPath, string serial)
    {
        if (!File.Exists(adbPath)) throw new FileNotFoundException("找不到 ADB 程序", adbPath);
        if (!GameTargetSelection.IsLocalAdbSerial(serial))
            throw new ArgumentException("仅支持本机 ADB 设备序列号", nameof(serial));
        var preference = new GameTargetPreference("adb", window.Title, window.ProcessPath, adbPath, serial);
        lock (_sourceGate)
        {
            GameSource.Select(preference, GameTargetSelection.FromManualWindow(preference, window));
            _settings.SaveGameTarget(preference);
        }
    }

    public void SelectNativeSource(GameTarget target)
    {
        var preference = new GameTargetPreference(target.Id);
        lock (_sourceGate)
        {
            GameSource.Select(preference, target);
            _settings.SaveGameTarget(preference);
        }
    }

    public void ScanNow() => _scanner?.EnqueueManualScan();
    public void ClearBans() => _scanner?.ClearBans();
    public void SetManualBan(string name, bool banned) => _scanner?.SetManualBan(name, banned);

    public async Task SaveTrackingAsync()
    {
        await _trackingSaveGate.WaitAsync();
        try { await _settings.SaveTrackingEntriesAsync(Tracking.AllEntries.ToList()); }
        finally { _trackingSaveGate.Release(); }
    }

    public void Dispose()
    {
        _windowTracker.Dispose();
        _autoTargetTimer.Dispose();
        _scanner?.Dispose();
        Site.Dispose();
        _trackingSaveGate.Dispose();
    }
}
