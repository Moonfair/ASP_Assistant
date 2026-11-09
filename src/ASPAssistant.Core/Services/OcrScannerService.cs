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
    private readonly Channel<(byte[] Screenshot, int Generation)> _manualScanChannel = Channel.CreateUnbounded<(byte[], int)>();
    private int _pendingManualScans;

    public event Action<GameState.GameState>? GameStateUpdated;

    /// <summary>
    /// Raised when covenant recognition or a manual override changes the bans.
    /// The argument is the current full list of banned operator names.
    /// </summary>
    public event Action<IReadOnlyList<string>>? BansDetected;
    public event Action<PreparationNotice>? PreparationStatusChanged;
    public IReadOnlyList<string> DetectedBans => _preparation?.BannedNames ?? [];

    /// <summary>
    /// Raised when the number of pending manual screenshot scans changes.
    /// Argument is the new pending count (0 = all done).
    /// </summary>
    public event Action<int>? ManualScanQueueChanged;

    public OcrScannerService(
        ScreenCaptureService captureService,
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
        _ocrStrategy = ocrStrategy;
        _shopRecognizer = new ShopRecognizer(ocrEngine, shopNames);
        _ocrEngine = ocrEngine;
        _gameState = gameState;
        _getTrackedNames = getTrackedNames;
        if (getAllOperators != null)
        {
            _preparation = new PreparationScanner(ocrEngine, getAllOperators(), covenantCatalog: covenantCatalog);
            _preparation.BansChanged += names => BansDetected?.Invoke(names);
            _preparation.StatusChanged += notice => PreparationStatusChanged?.Invoke(notice);
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

        if (_disposed || !await _recognitionGate.WaitAsync(0))
        {
            Interlocked.Increment(ref _skippedTicks);
            Interlocked.Exchange(ref _scanning, 0);
            return;
        }
        var timing = Stopwatch.StartNew();
        long captureMs = 0, preparationMs = 0, targetMs = 0, shopMs = 0, publishMs = 0;
        try
        {
            var screenshot = _captureService.CaptureScreen();
            captureMs = timing.ElapsedMilliseconds;
            if (screenshot == null)
            {
                _preparation?.NotifyCaptureUnavailable();
                return;
            }

            var (imgWidth, imgHeight) = GetPngDimensions(screenshot);
            if (imgWidth <= 0 || imgHeight <= 0) return;
            // Preparation is independent of the shop tracking selection.
            if (_preparation != null)
            {
                await _preparation.ProcessAsync(screenshot);
                preparationMs = timing.ElapsedMilliseconds - captureMs;
                if (_preparation.Phase != PreparationPhase.None) return;
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

            // Only raise GameStateUpdated when the stable operator set actually changes.
            if (_stabilizer.TryUpdate(rawItems, out var stableItems))
            {
                _gameState.ShopItems = stableItems.ToList();
                GameStateUpdated?.Invoke(_gameState);
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
    public void EnqueueManualScan()
    {
        var screenshot = _captureService.CaptureScreen();
        if (screenshot == null)
        {
            AppLogger.Warn("OcrScanner", "EnqueueManualScan: screenshot capture returned null");
            return;
        }
        var pending = Interlocked.Increment(ref _pendingManualScans);
        AppLogger.Info("OcrScanner", $"Manual scan enqueued (pending={pending}, png={screenshot.Length} bytes)");
        ManualScanQueueChanged?.Invoke(pending);
        _manualScanChannel.Writer.TryWrite((screenshot, _preparation?.Generation ?? 0));
    }

    private async Task ProcessManualScansAsync()
    {
        await foreach (var request in _manualScanChannel.Reader.ReadAllAsync())
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _recognitionGate.WaitAsync();
            try
            {
                if (_disposed) continue;
                var screenshot = request.Screenshot;
                var (w, h) = GetPngDimensions(screenshot);
                AppLogger.Info("OcrScanner", $"Manual scan started (img={w}x{h})");
                if (_preparation != null)
                    await _preparation.ProcessAsync(screenshot, force: true, expectedGeneration: request.Generation);
                AppLogger.Info("OcrScanner", $"Manual scan completed in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                AppLogger.Error("OcrScanner", "Manual scan failed", ex);
            }
            finally
            {
                _recognitionGate.Release();
                var remaining = Interlocked.Decrement(ref _pendingManualScans);
                ManualScanQueueChanged?.Invoke(remaining);
            }
        }
    }

    /// <summary>
    /// Reads width and height from a PNG file's IHDR chunk (bytes 16–23).
    /// No dependencies beyond the raw byte array.
    /// </summary>
    private static (int Width, int Height) GetPngDimensions(byte[] png)
    {
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    public void SetManualBan(string name, bool banned) => _preparation?.SetManualBan(name, banned);
    public void ClearBans() => _preparation?.ClearBans();

    public void Dispose()
    {
        _scanTimer.Stop();
        _scanTimer.Dispose();
        _manualScanChannel.Writer.TryComplete();
        _disposed = true;
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
