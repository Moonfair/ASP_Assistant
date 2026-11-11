using System.Threading.Channels;
using System.Diagnostics;
using System.Timers;
using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class OcrScannerService : IDisposable
{
    private readonly ScreenCaptureService _captureService;
    private readonly GameTargetSelection _selection;
    private GameTarget? _recognitionTarget;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly ShopRecognizer _shopRecognizer;
    private readonly IOcrEngine _ocrEngine;
    private readonly GameState.GameState _gameState;
    private readonly Func<IReadOnlyList<string>> _getTrackedNames;
    private readonly PreparationScanner? _preparation;
    private readonly SemaphoreSlim _recognitionGate = new(1, 1);
    private volatile bool _disposed;
    private readonly Timer _scanTimer;
    private readonly ShopStateStabilizer _stabilizer = new();
    private int _scanning; // re-entry guard (0=idle, 1=busy)
    private int _skippedTicks;
    private long _lastTimingLog;


    // Manual screenshot scan queue — button-triggered scans execute sequentially.
    private readonly Channel<ManualScan> _manualScanChannel = Channel.CreateUnbounded<ManualScan>();
    private int _manualGeneration;
    private int _pendingManualScans;

    public event Action<GameTarget?, GameState.GameState>? GameStateUpdated;

    /// <summary>
    /// Raised when covenant recognition or a manual override changes the bans.
    /// The argument is the current full list of banned operator names.
    /// </summary>
    public event Action<GameTarget?, IReadOnlyList<string>>? BansDetected;
    public event Action<GameTarget?, PreparationNotice>? PreparationStatusChanged;
    public IReadOnlyList<string> DetectedBans => _preparation?.BannedNames ?? [];
    public event Action<GameTarget?, CovenantAvailability?>? CovenantAvailabilityChanged;
    public CovenantAvailability? CovenantAvailability => _preparation?.CovenantAvailability;

    /// <summary>
    /// Raised when the number of pending manual screenshot scans changes.
    /// Argument is the new pending count (0 = all done).
    /// </summary>
    public event Action<int>? ManualScanQueueChanged;

    public OcrScannerService(
        ScreenCaptureService captureService,
        GameTargetSelection selection,
        IOcrStrategy ocrStrategy,
        IOcrEngine ocrEngine,
        GameState.GameState gameState,
        Func<IReadOnlyList<string>> getTrackedNames,
        Func<IReadOnlyList<BanCandidate>>? getAllOperators = null,
        CovenantCatalog? covenantCatalog = null,
        int intervalMs = 3000,
        IReadOnlyList<string>? shopNames = null)
    {
        _captureService = captureService;
        _selection = selection;
        _ocrStrategy = ocrStrategy;
        _shopRecognizer = new ShopRecognizer(ocrEngine, shopNames);
        _ocrEngine = ocrEngine;
        _gameState = gameState;
        _getTrackedNames = getTrackedNames;
        if (getAllOperators != null)
        {
            _preparation = new PreparationScanner(ocrEngine, getAllOperators(), covenantCatalog: covenantCatalog);
            _preparation.BansChanged += names =>
            {
                if (IsCurrentTarget) BansDetected?.Invoke(_recognitionTarget, names);
            };
            _preparation.CovenantAvailabilityChanged += state =>
            {
                if (IsCurrentTarget) CovenantAvailabilityChanged?.Invoke(_recognitionTarget, state);
            };
            _preparation.StatusChanged += notice =>
            {
                if (IsCurrentTarget) PreparationStatusChanged?.Invoke(_recognitionTarget, notice);
            };
        }
        _scanTimer = new Timer(intervalMs);
        _scanTimer.Elapsed += OnScanTick;

        // Start the background consumer for button-triggered manual scans.
        _ = Task.Run(ProcessManualScansAsync);
    }

    public void Start() => _scanTimer.Start();

    private async void OnScanTick(object? sender, ElapsedEventArgs e)
    {
        // Skip if a previous scan is still running.
        if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0)
        {
            Interlocked.Increment(ref _skippedTicks);
            return;
        }

        if (_disposed || Volatile.Read(ref _pendingManualScans) != 0 || !await _recognitionGate.WaitAsync(0))
        {
            Interlocked.Increment(ref _skippedTicks);
            Interlocked.Exchange(ref _scanning, 0);
            return;
        }
        var timing = Stopwatch.StartNew();
        long captureMs = 0, preparationMs = 0, targetMs = 0, shopMs = 0, publishMs = 0;
        try
        {
            var target = _selection.Current;
            UseTarget(target);
            var screenshot = target == null ? null : await _captureService.CaptureAsync(target, _selection);
            captureMs = timing.ElapsedMilliseconds;
            if (!IsCurrentTarget) return;
            if (screenshot == null)
            {
                _preparation?.NotifyCaptureUnavailable();
                if (target is { Kind: not GameTargetKind.Windows } && _gameState.ShopItems.Count > 0)
                {
                    _stabilizer.Reset();
                    _gameState.ShopItems = [];
                    if (IsCurrentTarget) GameStateUpdated?.Invoke(target, _gameState);
                }
                return;
            }

            var (imgWidth, imgHeight) = ScreenCaptureService.GetPngSize(screenshot);
            if (imgWidth <= 0 || imgHeight <= 0) return;
            // Preparation is independent of the shop tracking selection.
            if (_preparation != null)
            {
                await _preparation.ProcessAsync(screenshot);
                preparationMs = timing.ElapsedMilliseconds - captureMs;
                if (!IsCurrentTarget || _preparation.Phase != PreparationPhase.None) return;
            }

            var regions = _ocrStrategy.GetScanRegions();

            var shopRegion = regions.FirstOrDefault(r => r.Name == "Shop");
            if (shopRegion == null)
                return;

            var trackedNames = _getTrackedNames();
            targetMs = timing.ElapsedMilliseconds - captureMs - preparationMs;
            var rawItems = await _shopRecognizer.RecognizeAsync(
                screenshot, imgWidth, imgHeight, trackedNames, shopRegion);
            shopMs = timing.ElapsedMilliseconds - captureMs - preparationMs - targetMs;
            if (!IsCurrentTarget) return;

            // Only raise GameStateUpdated when the stable operator set actually changes.
            if (_stabilizer.TryUpdate(rawItems, out var stableItems))
            {
                _gameState.ShopItems = stableItems.ToList();
                GameStateUpdated?.Invoke(target, _gameState);
            }
            publishMs = timing.ElapsedMilliseconds - captureMs - preparationMs - targetMs - shopMs;
        }
        catch (Exception ex)
        {
            // Swallow unexpected errors (e.g. capture failure during window state change)
            // so the timer-driven async void method never propagates an unhandled exception.
            AppLogger.Error("OcrScanner", "Scan tick failed", ex);
        }
        finally
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastTimingLog == 0 || Stopwatch.GetElapsedTime(_lastTimingLog, now) >= TimeSpan.FromSeconds(10))
            {
                _lastTimingLog = now;
                AppLogger.Info("OcrScanner", $"Scan timing: total={timing.ElapsedMilliseconds}ms capture={captureMs}ms "
                    + $"preparation={preparationMs}ms targets={targetMs}ms shop={shopMs}ms publish={publishMs}ms "
                    + $"interval={_scanTimer.Interval}ms skippedTicks={Interlocked.Exchange(ref _skippedTicks, 0)} "
                    + $"pendingManual={Volatile.Read(ref _pendingManualScans)}");
            }
            _recognitionGate.Release();
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    /// <summary>
    /// Captures the current game window screenshot and enqueues it for ban-screen matching.
    /// The scan runs on a background consumer thread so multiple calls queue up sequentially.
    /// </summary>
    public async void EnqueueManualScan()
    {
        var target = _selection.Current;
        if (_disposed || target == null) return;
        int generation = Volatile.Read(ref _manualGeneration);
        var pending = Interlocked.Increment(ref _pendingManualScans);
        ManualScanQueueChanged?.Invoke(pending);
        bool queued = false;
        try
        {
            var screenshot = await _captureService.CaptureAsync(target, _selection);
            if (screenshot != null && ReferenceEquals(target, _selection.Current))
                queued = _manualScanChannel.Writer.TryWrite(new ManualScan(target, screenshot, generation));
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex) { AppLogger.Error("OcrScanner", "Manual screenshot failed", ex); }
        finally { if (!queued) FinishManualScan(); }
    }

    private async Task ProcessManualScansAsync()
    {
        await foreach (var request in _manualScanChannel.Reader.ReadAllAsync())
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _recognitionGate.WaitAsync();
            try
            {
                if (_disposed || !ReferenceEquals(request.Target, _selection.Current) ||
                    request.Generation != Volatile.Read(ref _manualGeneration)) continue;
                UseTarget(request.Target);
                int? preparationGeneration = _preparation?.Generation;
                if (!IsCurrentTarget || request.Generation != Volatile.Read(ref _manualGeneration)) continue;
                var screenshot = request.Screenshot;
                var (w, h) = ScreenCaptureService.GetPngSize(screenshot);
                AppLogger.Info("OcrScanner", $"Manual scan started (img={w}x{h})");
                if (_preparation != null)
                    await _preparation.ProcessAsync(screenshot, force: true, expectedGeneration: preparationGeneration);
                AppLogger.Info("OcrScanner", $"Manual scan completed in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                AppLogger.Error("OcrScanner", "Manual scan failed", ex);
            }
            finally
            {
                _recognitionGate.Release();
                FinishManualScan();
            }
        }
    }

    private void FinishManualScan()
    {
        var remaining = Interlocked.Decrement(ref _pendingManualScans);
        ManualScanQueueChanged?.Invoke(remaining);
    }

    private bool IsCurrentTarget => ReferenceEquals(_recognitionTarget, _selection.Current);

    private void UseTarget(GameTarget? target)
    {
        if (ReferenceEquals(_recognitionTarget, target)) return;
        _recognitionTarget = target;
        _stabilizer.Reset();
        _preparation?.ResetTarget();
        _gameState.ShopItems = [];
        if (IsCurrentTarget) GameStateUpdated?.Invoke(target, _gameState);
    }

    public void SetManualBan(string name, bool banned) => _preparation?.SetManualBan(name, banned);
    public void ClearBans()
    {
        Interlocked.Increment(ref _manualGeneration);
        _preparation?.ClearBans();
    }

    private sealed record ManualScan(GameTarget Target, byte[] Screenshot, int Generation);

    public void Dispose()
    {
        _disposed = true;
        _scanTimer.Stop();
        _scanTimer.Dispose();
        _manualScanChannel.Writer.TryComplete();
        _captureService.Dispose();
        // Let in-flight OCR finish before releasing its native taskers.
        _ = Task.Run(async () =>
        {
            await _recognitionGate.WaitAsync();
            try
            {
                if (_ocrEngine is IDisposable disposableOcr) disposableOcr.Dispose();
            }
            finally { _recognitionGate.Release(); }
        });
    }
}
