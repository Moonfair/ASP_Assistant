using System.Buffers.Binary;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.Interop;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

public sealed class ScreenCaptureService : IDisposable
{
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly AdbScreenCapture _adb = new();
    private GameTarget? _active;
    private MaaWin32Controller? _windowCapture;
    private DateTimeOffset _windowRetryAfter;
    private FrameGeometry? _geometry;

    public RECT? GetViewport(GameTarget target)
    {
        if (target.GetRenderRect() is not { } rect) return null;
        var geometry = Volatile.Read(ref _geometry);
        if (target.Kind == GameTargetKind.Windows) return rect;
        if (geometry == null || !ReferenceEquals(geometry.Target, target)) return null;
        if (geometry.RenderChild != 0 && User32.GetRootWindow(geometry.RenderChild) == target.Window &&
            User32.IsWindowVisible(geometry.RenderChild) &&
            User32.GetClientRectScreen(geometry.RenderChild) is { Width: > 0, Height: > 0 } child)
            rect = child;
        return GameViewport.Fit(rect, geometry.Width, geometry.Height);
    }

    public async Task<byte[]?> CaptureAsync(GameTarget target, GameTargetSelection? selection = null)
    {
        await _captureGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            if (selection != null && !ReferenceEquals(selection.Current, target)) return null;
            if (!ReferenceEquals(_active, target))
            {
                _adb.Dispose();
                _windowCapture?.Dispose();
                _windowCapture = null;
                _windowRetryAfter = default;
                Volatile.Write(ref _geometry, null);
                _active = target;
            }
            if (target.GetRenderRect() == null)
            {
                Volatile.Write(ref _geometry, null);
                return null;
            }

            byte[]? image = null;
            if (target.Kind != GameTargetKind.Windows)
                image = await _adb.CaptureAsync(target, _shutdown.Token).ConfigureAwait(false);
            if (selection != null && !ReferenceEquals(selection.Current, target)) return null;
            bool fromAdb = image != null;

            image ??= await Task.Run(() =>
            {
                if (target.GetRenderRect() is not { } rect) return null;
                if (target.Kind != GameTargetKind.Windows)
                {
                    var captured = CaptureWindow(target, rect);
                    if (captured != null) return captured;
                    if (User32.GetRootWindow(User32.GetForegroundWindow()) != target.Window) return null;
                }
                return CopyScreen(rect);
            }, _shutdown.Token).ConfigureAwait(false);

            var (width, height) = GetPngSize(image);
            if (selection != null && !ReferenceEquals(selection.Current, target)) return null;
            if (width <= 0)
            {
                Volatile.Write(ref _geometry, null);
                return null;
            }
            nint child = fromAdb && target.Kind == GameTargetKind.Adb && target.GetRenderRect() is { } bounds
                ? User32.FindRenderChild(target.Window, bounds, width, height) : 0;
            Volatile.Write(ref _geometry, new FrameGeometry(target, width, height, child));
            return image;
        }
        finally { _captureGate.Release(); }
    }

    private byte[]? CaptureWindow(GameTarget target, RECT render)
    {
        if (DateTimeOffset.UtcNow < _windowRetryAfter ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)) return null;
        try
        {
            if (_windowCapture == null)
            {
                _windowCapture = new MaaWin32Controller(target.Window, Win32ScreencapMethod.FramePool,
                    Win32InputMethod.SendMessage, Win32InputMethod.SendMessage);
                _windowCapture.SetOption(ControllerOption.ScreenshotUseRawSize, true);
            }

            using var buffer = new MaaImageBuffer();
            if (_windowCapture.Screencap().Wait() != MaaJobStatus.Succeeded || !_windowCapture.GetCachedImage(buffer) ||
                !buffer.TryGetEncodedData(out byte[]? png) || png == null)
                return DisableWindowCapture();
            if (target.RenderWindow == target.Window) return png;

            if (target.GetRenderRect() is not { } after || !render.Equals(after) ||
                User32.GetClientRectScreen(target.Window) is not { } client)
                return null;

            // FramePool returns the outer window's client image. A rounded border can cost a pixel.
            var (width, height) = GetPngSize(png);
            if (Math.Abs(width - client.Width) > 2 || Math.Abs(height - client.Height) > 2) return null;
            var area = Rectangle.Intersect(new Rectangle(0, 0, width, height),
                new Rectangle(render.Left - client.Left, render.Top - client.Top, render.Width, render.Height));
            if (area.Width < render.Width - 2 || area.Height < render.Height - 2 || area.IsEmpty) return null;

            using var source = new MemoryStream(png);
            using var bitmap = new Bitmap(source);
            using var cropped = bitmap.Clone(area, bitmap.PixelFormat);
            return Encode(cropped);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or MaaJobStatusException)
        {
            AppLogger.Warn("ScreenCapture", $"Window capture unavailable for {target.Name}: {ex.Message}");
            return DisableWindowCapture();
        }
    }

    private byte[]? DisableWindowCapture()
    {
        _windowCapture?.Dispose();
        _windowCapture = null;
        _windowRetryAfter = DateTimeOffset.UtcNow.AddSeconds(3);
        return null;
    }

    private static byte[]? CopyScreen(RECT rect)
    {
        try
        {
            using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
            return Encode(bitmap);
        }
        catch (Win32Exception ex)
        {
            AppLogger.Warn("ScreenCapture", $"Screen copy failed ({ex.NativeErrorCode}): {ex.Message}");
            return null;
        }
    }

    private static byte[] Encode(Bitmap bitmap)
    {
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    internal static (int Width, int Height) GetPngSize(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> pngHeader = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82];
        if (bytes.Length < 33 || !bytes[..16].SequenceEqual(pngHeader)) return default;
        int width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
        int height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
        return width > 0 && height > 0 ? (width, height) : default;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        // Native capture may still be returning. Its owner releases it after the current call.
        _ = Task.Run(async () =>
        {
            await _captureGate.WaitAsync().ConfigureAwait(false);
            try
            {
                _adb.Dispose();
                _windowCapture?.Dispose();
            }
            finally { _captureGate.Release(); }
        });
    }

    private sealed record FrameGeometry(GameTarget Target, int Width, int Height, nint RenderChild);
}
