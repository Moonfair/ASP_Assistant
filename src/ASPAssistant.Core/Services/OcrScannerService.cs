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
    private readonly Timer _scanTimer;
    private readonly ShopStateStabilizer _stabilizer = new();
    private int _scanning; // re-entry guard (0=idle, 1=busy)

    public event Action<GameState.GameState>? GameStateUpdated;

    public OcrScannerService(
        ScreenCaptureService captureService,
        IOcrStrategy ocrStrategy,
        ICardDetector cardDetector,
        IOcrEngine ocrEngine,
        GameState.GameState gameState,
        Func<IReadOnlyList<string>> getTrackedNames,
        int intervalMs = 3000)
    {
        _captureService = captureService;
        _ocrStrategy = ocrStrategy;
        _cardDetector = cardDetector;
        _ocrEngine = ocrEngine;
        _gameState = gameState;
        _getTrackedNames = getTrackedNames;
        _scanTimer = new Timer(intervalMs);
        _scanTimer.Elapsed += OnScanTick;
    }

    public void Start() => _scanTimer.Start();
    public void Stop() => _scanTimer.Stop();

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
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
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
                // Use Contains: the game renders names with decorators like "●深靛" or "入隐现".
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

    public void Dispose()
    {
        _scanTimer.Stop();
        _scanTimer.Dispose();
    }
}
