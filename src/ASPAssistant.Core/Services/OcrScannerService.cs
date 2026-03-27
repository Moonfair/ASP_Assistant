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
    private readonly WindowsOcrEngine _ocrEngine;
    private readonly GameState.GameState _gameState;
    private readonly IReadOnlyList<string> _knownOperatorNames;
    private readonly Timer _scanTimer;

    public event Action<GameState.GameState>? GameStateUpdated;

    public OcrScannerService(
        ScreenCaptureService captureService,
        IOcrStrategy ocrStrategy,
        WindowsOcrEngine ocrEngine,
        GameState.GameState gameState,
        IReadOnlyList<string> knownOperatorNames,
        int intervalSeconds = 3)
    {
        _captureService = captureService;
        _ocrStrategy = ocrStrategy;
        _ocrEngine = ocrEngine;
        _gameState = gameState;
        _knownOperatorNames = knownOperatorNames;
        _scanTimer = new Timer(intervalSeconds * 1000);
        _scanTimer.Elapsed += OnScanTick;
    }

    public void Start() => _scanTimer.Start();
    public void Stop() => _scanTimer.Stop();

    private async void OnScanTick(object? sender, ElapsedEventArgs e)
    {
        var screenshot = _captureService.CaptureScreen();
        if (screenshot == null)
            return;

        // Get game window client size for percentage → pixel conversion
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

        var ocrText = await _ocrEngine.RecognizeRegionAsync(screenshot, rx, ry, rw, rh);

        var foundItems = MatchOperatorNames(ocrText, shopRegion);
        _gameState.ShopItems = foundItems;

        GameStateUpdated?.Invoke(_gameState);
    }

    /// <summary>
    /// Checks which known operator names appear in the OCR text
    /// and creates ShopItem entries with the shop region coordinates.
    /// </summary>
    private List<ShopItem> MatchOperatorNames(string ocrText, OcrRegionDefinition shopRegion)
    {
        var items = new List<ShopItem>();
        if (string.IsNullOrWhiteSpace(ocrText))
            return items;

        foreach (var name in _knownOperatorNames)
        {
            if (ocrText.Contains(name, StringComparison.Ordinal))
            {
                items.Add(new ShopItem
                {
                    Name = name,
                    OcrRegion = (shopRegion.XPercent, shopRegion.YPercent,
                                 shopRegion.WidthPercent, shopRegion.HeightPercent)
                });
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
