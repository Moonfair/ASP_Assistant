using System.Threading.Channels;
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
    private readonly ICardDetector _cardDetector;
    private readonly IOcrEngine _ocrEngine;
    private readonly GameState.GameState _gameState;
    private readonly Func<IReadOnlyList<string>> _getTrackedNames;
    private readonly Func<IReadOnlyList<(string Name, IReadOnlyList<string> CoreCovenants, IReadOnlyList<string> AdditionalCovenants, IReadOnlyList<string> TemplateNames)>>? _getAllOperators;
    private readonly MaaBanIconMatcher? _iconMatcher;
    private readonly Timer _scanTimer;
    private readonly ShopStateStabilizer _stabilizer = new();
    private int _scanning; // re-entry guard (0=idle, 1=busy)

    // Accumulated ban set for manual screenshot scans.
    private readonly HashSet<string> _accumulatedBans = [];

    // Pool of additional MaaBanIconMatcher instances for parallel per-slot SIFT.
    // Initialised in the background so app startup is not delayed.
    private MaaBanIconMatcher[]? _matcherPool;
    private readonly Task? _poolInitTask;

    // Manual screenshot scan queue — button-triggered scans execute sequentially.
    private readonly Channel<byte[]> _manualScanChannel = Channel.CreateUnbounded<byte[]>();
    private int _pendingManualScans;

    public event Action<GameState.GameState>? GameStateUpdated;

    /// <summary>
    /// Raised when manual screenshot matching finds newly banned operators.
    /// The argument is the full accumulated list of banned operator names.
    /// </summary>
    public event Action<IReadOnlyList<string>>? BansDetected;

    /// <summary>
    /// Raised when the number of pending manual screenshot scans changes.
    /// Argument is the new pending count (0 = all done).
    /// </summary>
    public event Action<int>? ManualScanQueueChanged;

    public OcrScannerService(
        ScreenCaptureService captureService,
        IOcrStrategy ocrStrategy,
        ICardDetector cardDetector,
        IOcrEngine ocrEngine,
        GameState.GameState gameState,
        Func<IReadOnlyList<string>> getTrackedNames,
        Func<IReadOnlyList<(string Name, IReadOnlyList<string> CoreCovenants, IReadOnlyList<string> AdditionalCovenants, IReadOnlyList<string> TemplateNames)>>? getAllOperators = null,
        MaaBanIconMatcher? iconMatcher = null,
        int intervalMs = 3000)
    {
        _captureService = captureService;
        _ocrStrategy = ocrStrategy;
        _cardDetector = cardDetector;
        _ocrEngine = ocrEngine;
        _gameState = gameState;
        _getTrackedNames = getTrackedNames;
        _getAllOperators = getAllOperators;
        _iconMatcher = iconMatcher;
        _scanTimer = new Timer(intervalMs);
        _scanTimer.Elapsed += OnScanTick;

        // Initialise extra matcher instances in the background so the main thread is not blocked.
        // By the time the first ban screen appears the pool will already be ready.
        if (iconMatcher != null)
        {
            _poolInitTask = Task.Run(() =>
            {
                try
                {
                    const int ExtraInstances = 14; // primary + 14 extras = pool of 15
                    var extras = Enumerable.Range(0, ExtraInstances)
                        .Select(_ => new MaaBanIconMatcher(iconMatcher.DataDir)
                        {
                            OperatorFeatureCount = iconMatcher.OperatorFeatureCount,
                            FeatureDetector      = iconMatcher.FeatureDetector,
                            OperatorRatio        = iconMatcher.OperatorRatio,
                            BanRoiX              = iconMatcher.BanRoiX,
                            BanRoiY              = iconMatcher.BanRoiY,
                            BanRoiW              = iconMatcher.BanRoiW,
                            BanRoiH              = iconMatcher.BanRoiH,
                        })
                        .ToArray();
                    // Pool = [primary, extra0..extra13] — primary is index 0
                    _matcherPool = [iconMatcher, .. extras];
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[OcrScannerService] Matcher pool init failed: {ex.Message}");
                    _matcherPool = [iconMatcher];
                }
            });
        }

        // Start the background consumer for button-triggered manual scans.
        _ = Task.Run(ProcessManualScansAsync);
    }

    public void Start() => _scanTimer.Start();

    private async void OnScanTick(object? sender, ElapsedEventArgs e)
    {
        // Skip if a previous scan is still running.
        if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0)
            return;

        try
        {
            var screenshot = _captureService.CaptureScreen();
            if (screenshot == null)
                return;

            var hwnd = User32.FindArknightsWindow();
            if (hwnd == IntPtr.Zero)
                return;

            if (!User32.GetClientRect(hwnd, out var clientRect))
                return;

            int imgWidth = clientRect.Width;
            int imgHeight = clientRect.Height;
            if (imgWidth <= 0 || imgHeight <= 0)
                return;

            var regions = _ocrStrategy.GetScanRegions();

            var shopRegion = regions.FirstOrDefault(r => r.Name == "Shop");
            if (shopRegion == null)
                return;

            int rx = (int)(shopRegion.XPercent * imgWidth);
            int ry = (int)(shopRegion.YPercent * imgHeight);
            int rw = (int)(shopRegion.WidthPercent * imgWidth);
            int rh = (int)(shopRegion.HeightPercent * imgHeight);

            var trackedNames = _getTrackedNames();

            if (trackedNames.Count == 0)
                return;

            // Step 1: Detect all operator card boundaries in the shop region.
            var cardBoxes = await _cardDetector.DetectCardsAsync(screenshot, rx, ry, rw, rh);

            if (cardBoxes.Count == 0)
                return;

            // Step 2: For each card, OCR only its name strip and match against tracked names.
            var rawItems = await MatchTrackedOperatorsAsync(
                screenshot, cardBoxes, trackedNames, imgWidth, imgHeight);

            // Only raise GameStateUpdated when the stable operator set actually changes.
            if (_stabilizer.TryUpdate(rawItems, out var stableItems))
            {
                _gameState.ShopItems = stableItems.ToList();
                GameStateUpdated?.Invoke(_gameState);
            }
        }
        catch (Exception)
        {
            // Swallow unexpected errors (e.g. capture failure during window state change)
            // so the timer-driven async void method never propagates an unhandled exception.
        }
        finally
        {
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
        if (screenshot == null) return;
        Interlocked.Increment(ref _pendingManualScans);
        ManualScanQueueChanged?.Invoke(_pendingManualScans);
        _manualScanChannel.Writer.TryWrite(screenshot);
    }

    private async Task ProcessManualScansAsync()
    {
        var regions = _ocrStrategy.GetScanRegions();
        await foreach (var screenshot in _manualScanChannel.Reader.ReadAllAsync())
        {
            try
            {
                var (w, h) = GetPngDimensions(screenshot);
                await RunBanMatchCoreAsync(screenshot, regions, w, h);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OcrScannerService] Manual scan failed: {ex.Message}");
            }
            finally
            {
                ManualScanQueueChanged?.Invoke(Interlocked.Decrement(ref _pendingManualScans));
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

    /// <summary>
    /// Core ban-screen matching: faction OCR → candidate filter → batch FeatureMatch.
    /// Accumulates hits in <see cref="_accumulatedBans"/> and fires <see cref="BansDetected"/>.
    /// </summary>
    private async Task RunBanMatchCoreAsync(
        byte[] screenshot,
        IReadOnlyList<OcrRegionDefinition> regions,
        int imgWidth,
        int imgHeight)
    {
        // ── Faction OCR: detect which covenant groups are visible in the left panel ──────────
        var allOperators = _getAllOperators!();
        var allCovenants = allOperators
            .SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet();

        var visibleFactions = new HashSet<string>();
        var factionRegion = regions.FirstOrDefault(r => r.Name == "BanFactionPanel");
        if (factionRegion != null)
        {
            int fx = (int)(factionRegion.XPercent * imgWidth);
            int fy = (int)(factionRegion.YPercent * imgHeight);
            int fw = (int)(factionRegion.WidthPercent * imgWidth);
            int fh = (int)(factionRegion.HeightPercent * imgHeight);
            var factionOcr = await _ocrEngine.RecognizeRegionAsync(screenshot, fx, fy, fw, fh);
            foreach (var result in factionOcr)
                foreach (var covenant in allCovenants)
                    if (result.Text.Contains(covenant))
                        visibleFactions.Add(covenant);
        }

        // Build candidate list, skipping already-identified bans and filtering by visible faction.
        var candidateSnapshot = allOperators
            .Where(x => !_accumulatedBans.Contains(x.Name))
            .Where(x => visibleFactions.Count == 0
                || x.CoreCovenants.Any(c => visibleFactions.Contains(c))
                || x.AdditionalCovenants.Any(c => visibleFactions.Contains(c)))
            .Select(x => (x.Name, x.TemplateNames))
            .ToList();

        // Use the pool when available; fall back to the primary matcher if still initialising.
        var pool = (_matcherPool is { Length: > 0 }) ? _matcherPool : new[] { _iconMatcher! };

        bool anyNewMatch = false;
        var matchSw = System.Diagnostics.Stopwatch.StartNew();
        for (int batchStart = 0; batchStart < candidateSnapshot.Count; batchStart += pool.Length)
        {
            var batch = candidateSnapshot.Skip(batchStart).Take(pool.Length).ToList();

            var batchTasks = batch.Select((candidate, i) =>
            {
                var matcher = pool[i];
                return matcher.FindBestInSlotAsync(screenshot, candidate, imgWidth, imgHeight);
            }).ToList();

            var batchResults = await Task.WhenAll(batchTasks);

            foreach (var tuple in batchResults)
            {
                if (tuple == null)
                    continue;

                var (name, hit, hitBox) = tuple.Value;

                if (hit && !_accumulatedBans.Contains(name))
                {
                    _accumulatedBans.Add(name);
                    anyNewMatch = true;
                }
            }
        }
        matchSw.Stop();
        System.Diagnostics.Debug.WriteLine(
            $"[BanMatch] {candidateSnapshot.Count} operators ({candidateSnapshot.Sum(c => c.TemplateNames.Count)} templates), pool={pool.Length}, elapsed={matchSw.ElapsedMilliseconds}ms");

        if (anyNewMatch)
            BansDetected?.Invoke(_accumulatedBans.ToList());
    }

    private async Task<List<ShopItem>> MatchTrackedOperatorsAsync(
        byte[] screenshot,
        IReadOnlyList<(int X, int Y, int W, int H)> cardBoxes,
        IReadOnlyList<string> trackedNames,
        int imgWidth,
        int imgHeight)
    {
        var items = new List<ShopItem>();

        foreach (var card in cardBoxes)
        {
            // The operator name is displayed above the detected card boundary.
            // Scan 80px above card top through the full card body to cover wherever it lands.
            int nameX = card.X;
            int nameY = Math.Max(0, card.Y - 80);
            int nameW = card.W;
            int nameH = Math.Max(1, card.H + 80);

            var ocrResults = await _ocrEngine.RecognizeRegionAsync(
                screenshot, nameX, nameY, nameW, nameH);

            foreach (var name in trackedNames)
            {
                // Use Contains: the game renders names with decorators like "???? or "????.
                if (!ocrResults.Any(r => r.Text.Contains(name, StringComparison.Ordinal)))
                    continue;

                double nx = card.X / (double)imgWidth;
                double ny = card.Y / (double)imgHeight;
                double nw = card.W / (double)imgWidth;
                double nh = card.H / (double)imgHeight;

                items.Add(new ShopItem
                {
                    // Use card X-pixel as a discriminator so two cards with
                    // the same operator name get distinct IDs.
                    Id = $"{name}@{card.X}",
                    Name = name,
                    OcrRegion = (nx, ny, nw, nh),
                });
                break;
            }
        }

        return items;
    }

    /// <summary>
    /// Mirrors a manual ban/unban into <see cref="_accumulatedBans"/> so subsequent scans
    /// don't re-detect (and re-ban) an operator the user has deliberately unbanned.
    /// </summary>
    public void SetManualBan(string name, bool banned)
    {
        if (banned) _accumulatedBans.Add(name);
        else _accumulatedBans.Remove(name);
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _scanTimer.Dispose();
        _manualScanChannel.Writer.TryComplete();
        // Dispose the extra pool instances (index 1+). Index 0 is _iconMatcher, owned externally.
        if (_matcherPool != null)
        {
            for (int i = 1; i < _matcherPool.Length; i++)
                _matcherPool[i].Dispose();
        }
    }
}
