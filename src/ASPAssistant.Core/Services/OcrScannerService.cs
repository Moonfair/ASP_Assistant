using System.Timers;
using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Models;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class OcrScannerService : IDisposable
{
    private readonly ScreenCaptureService _captureService;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly Timer _scanTimer;
    private readonly GameState.GameState _gameState;

    public event Action<GameState.GameState>? GameStateUpdated;

    public OcrScannerService(
        ScreenCaptureService captureService,
        IOcrStrategy ocrStrategy,
        GameState.GameState gameState,
        int intervalSeconds = 3)
    {
        _captureService = captureService;
        _ocrStrategy = ocrStrategy;
        _gameState = gameState;
        _scanTimer = new Timer(intervalSeconds * 1000);
        _scanTimer.Elapsed += OnScanTick;
    }

    public void Start() => _scanTimer.Start();
    public void Stop() => _scanTimer.Stop();

    private void OnScanTick(object? sender, ElapsedEventArgs e)
    {
        var screenshot = _captureService.CaptureScreen();
        if (screenshot == null)
            return;

        var regions = _ocrStrategy.GetScanRegions();

        // MaaFramework integration point:
        // For each region, crop the screenshot and run OCR.
        // Parse results and update GameState accordingly.

        GameStateUpdated?.Invoke(_gameState);
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _scanTimer.Dispose();
    }
}
