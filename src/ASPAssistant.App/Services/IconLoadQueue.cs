using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Media.Imaging;
using ASPAssistant.Core;

namespace ASPAssistant.App.Services;

/// <summary>One shared thumbnail cache and one bounded-batch background decoder.</summary>
public sealed class IconLoadQueue : IDisposable
{
    public static IconLoadQueue Shared { get; } = new(Path.Combine(AppContext.BaseDirectory, "data"));
    private const int BatchSize = 8;
    private readonly string _root;
    private readonly Func<string, BitmapSource?> _decode;
    private readonly Func<CancellationToken, Task> _yield;
    private readonly Dictionary<string, Task<BitmapSource?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<Request> _queue = Channel.CreateUnbounded<Request>(new() { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private bool _disposed;
    private sealed record Request(string Path, TaskCompletionSource<BitmapSource?> Completion);

    public IconLoadQueue(string root) : this(root, Decode, token => Task.Delay(12, token)) { }

    internal IconLoadQueue(string root, Func<string, BitmapSource?> decode, Func<CancellationToken, Task> yield)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        _decode = decode;
        _yield = yield;
        _ = Task.Run(() => ProcessAsync(_stop.Token));
    }

    public Task<BitmapSource?> RequestAsync(string? relativePath)
    {
        var path = Resolve(relativePath);
        if (path is null) return Task.FromResult<BitmapSource?>(null);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cache.TryGetValue(path, out var existing)) return existing;
            var completion = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cache.Add(path, completion.Task);
            _queue.Writer.TryWrite(new(path, completion));
            return completion.Task;
        }
    }

    public bool TryGetCached(string? relativePath, out BitmapSource? source)
    {
        source = null;
        var path = Resolve(relativePath);
        if (path is null) return true;
        lock (_gate)
        {
            if (!_cache.TryGetValue(path, out var task) || !task.IsCompletedSuccessfully) return false;
            source = task.Result;
            return true;
        }
    }

    private string? Resolve(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        try
        {
            var path = Path.GetFullPath(Path.Combine(_root, relativePath));
            return path.StartsWith(_root, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private async Task ProcessAsync(CancellationToken token)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                for (var count = 0; count < BatchSize && !token.IsCancellationRequested && _queue.Reader.TryRead(out var request); count++)
                {
                    BitmapSource? source = null;
                    try
                    {
                        source = _decode(request.Path);
                        if (source is not null && !source.IsFrozen) source.Freeze();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                        or ArgumentException or FormatException or InvalidOperationException or COMException)
                    {
                        source = null;
                        AppLogger.Error("Icons", $"Could not load icon: {request.Path}", ex);
                    }
                    request.Completion.TrySetResult(source);
                }
                // Keep CPU and result delivery bounded even when hundreds of bindings request images together.
                await _yield(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            while (_queue.Reader.TryRead(out var request)) request.Completion.TrySetCanceled();
        }
    }

    private static BitmapSource? Decode(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 128;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _queue.Writer.TryComplete();
            _stop.Cancel();
        }
    }
}
