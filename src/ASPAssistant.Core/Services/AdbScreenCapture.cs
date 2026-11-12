using System.ComponentModel;
using System.Text.Json;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

internal sealed class AdbScreenCapture : IDisposable
{
    private GameTarget? _target;
    private MaaAdbController? _controller;
    private string? _serial;
    private DateTimeOffset _retryAfter;

    public async Task<byte[]?> CaptureAsync(GameTarget target, CancellationToken token)
    {
        if (_target != target)
        {
            Dispose();
            _target = target;
            _retryAfter = DateTimeOffset.MinValue;
        }

        if (target.AdbPath is not { } adb || !File.Exists(adb) || DateTimeOffset.UtcNow < _retryAfter)
            return null;

        try
        {
            if (AdbConnection.GetLocalEndpoint(target) is not { } serial) return RetryLater();
            if (_serial == null)
                _serial = await AdbConnection.ConnectAsync(adb, serial, token).ConfigureAwait(false);
            if (_serial == null) return RetryLater();

            if (target.Kind is GameTargetKind.MuMu or GameTargetKind.LdPlayer)
            {
                byte[]? enhanced = await Task.Run(() => CaptureEnhanced(target, _serial), token).ConfigureAwait(false);
                if (enhanced != null) return enhanced;
            }

            var png = await TryAdbCaptureAsync(adb, ["-s", _serial, "exec-out", "screencap", "-p"], token)
                .ConfigureAwait(false);
            if (png != null) return png;

            // Some LDPlayer builds use the shell transport for binary screenshots.
            if (target.Kind == GameTargetKind.LdPlayer)
            {
                png = await TryAdbCaptureAsync(adb, ["-s", _serial, "shell", "-T", "screencap", "-p"], token)
                    .ConfigureAwait(false);
                if (png != null) return png;
            }
            return RetryLater();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or TimeoutException)
        {
            AppLogger.Warn("ScreenCapture", $"ADB screenshot unavailable for {target.Name}: {ex.Message}");
            return RetryLater();
        }
    }

    private static async Task<byte[]?> TryAdbCaptureAsync(string adb, string[] arguments, CancellationToken token)
    {
        try
        {
            var bytes = await LocalCommand.RunAsync(adb, arguments, token).ConfigureAwait(false);
            return ScreenCaptureService.GetPngSize(bytes) != default ? bytes : null;
        }
        catch (IOException) { return null; }
    }

    private DateTimeOffset _enhancementRetryAfter;

    private byte[]? CaptureEnhanced(GameTarget target, string serial)
    {
        if (DateTimeOffset.UtcNow < _enhancementRetryAfter) return null;
        try
        {
            if (_controller == null)
            {
                var config = new
                {
                    extras = new Dictionary<string, object>
                    {
                        [target.Kind == GameTargetKind.MuMu ? "mumu" : "ld"] =
                            new { enable = true, path = target.InstallDir, index = target.Instance, pid = target.VmPid }
                    }
                };
                _controller = new MaaAdbController(target.AdbPath!, serial, AdbScreencapMethods.EmulatorExtras,
                    AdbInputMethods.AdbShell, JsonSerializer.Serialize(config), AppContext.BaseDirectory);
                _controller.SetOption(ControllerOption.ScreenshotUseRawSize, true);
                AppLogger.Info("ScreenCapture", $"Connected enhanced screenshot backend for {target.Name}");
            }

            using var image = new MaaImageBuffer();
            return _controller.Screencap().Wait() == MaaJobStatus.Succeeded &&
                _controller.GetCachedImage(image) && image.TryGetEncodedData(out byte[]? png)
                && ScreenCaptureService.GetPngSize(png) != default ? png : DisableEnhancement();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or MaaJobStatusException)
        {
            AppLogger.Warn("ScreenCapture", $"Enhanced screenshot unavailable for {target.Name}: {ex.Message}");
            return DisableEnhancement();
        }
    }

    private byte[]? DisableEnhancement()
    {
        _controller?.Dispose();
        _controller = null;
        _enhancementRetryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
        return null;
    }

    private byte[]? RetryLater()
    {
        Dispose();
        _retryAfter = DateTimeOffset.UtcNow.AddSeconds(3);
        return null;
    }

    public void Dispose()
    {
        _controller?.Dispose();
        _controller = null;
        _serial = null;
        _enhancementRetryAfter = default;
    }
}
