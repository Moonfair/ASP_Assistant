using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Interop;
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

        var serial = target.Kind == GameTargetKind.LdPlayer ? target.AlternateSerial : target.Serial;
        if (target.AdbPath is not { } adb || serial == null ||
            !File.Exists(adb) || !GameTargetSelection.IsLocalAdbSerial(serial) || DateTimeOffset.UtcNow < _retryAfter)
            return null;

        try
        {
            if (target.Kind == GameTargetKind.LdPlayer && !OwnsLdEndpoint(target, serial)) return RetryLater();
            if (_serial == null)
                _serial = await ConnectAsync(adb, serial, token).ConfigureAwait(false);
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

    private static bool OwnsLdEndpoint(GameTarget target, string serial)
    {
        if (!IPEndPoint.TryParse(serial, out var endpoint) ||
            LocalTcpListener.FindOwner(endpoint.Port) is not { } owner)
            return false;
        User32.GetWindowThreadProcessId(target.Window, out var windowProcess);
        return owner == windowProcess || owner == target.VmPid;
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

    private static async Task<string?> ConnectAsync(string adb, string serial, CancellationToken token)
    {
        var devices = await LocalCommand.RunAsync(adb, ["devices"], token).ConfigureAwait(false);
        if (IsOnline(devices, serial)) return serial;

        if (serial.StartsWith("emulator-", StringComparison.Ordinal)) return null;
        await LocalCommand.RunAsync(adb, ["connect", serial], token).ConfigureAwait(false);
        devices = await LocalCommand.RunAsync(adb, ["devices"], token).ConfigureAwait(false);
        return IsOnline(devices, serial) ? serial : null;
    }

    private static bool IsOnline(byte[] output, string serial) =>
        Encoding.UTF8.GetString(output).Split('\n').Any(line => line.TrimEnd() == $"{serial}\tdevice");

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
