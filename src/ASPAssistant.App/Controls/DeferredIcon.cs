using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App.Services;

namespace ASPAssistant.App.Controls;

/// <summary>Defers uncached file work until after layout and applies results below input/render priority.</summary>
public static class DeferredIcon
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached(
        "Path", typeof(string), typeof(DeferredIcon), new PropertyMetadata("", OnPathChanged));
    public static readonly DependencyProperty IsGrayscaleProperty = DependencyProperty.RegisterAttached(
        "IsGrayscale", typeof(bool), typeof(DeferredIcon), new PropertyMetadata(false, OnGrayscaleChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(DeferredIcon));
    private static readonly ConditionalWeakTable<BitmapSource, BitmapSource> GrayscaleSources = new();
    private sealed class State
    {
        public int Version;
        public DispatcherOperation? Pending;
        public BitmapSource? Source;
    }

    public static string GetPath(DependencyObject target) => (string)target.GetValue(PathProperty);
    public static void SetPath(DependencyObject target, string value) => target.SetValue(PathProperty, value);
    public static bool GetIsGrayscale(DependencyObject target) => (bool)target.GetValue(IsGrayscaleProperty);
    public static void SetIsGrayscale(DependencyObject target, bool value) => target.SetValue(IsGrayscaleProperty, value);

    private static void OnGrayscaleChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is Image image && image.GetValue(StateProperty) is State state)
            SetSource(image, state, state.Source);
    }

    private static void OnPathChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Image image) return;
        if (image.GetValue(StateProperty) is not State)
        {
            image.SetValue(StateProperty, new State());
            image.Loaded += OnLoaded;
            image.Unloaded += OnUnloaded;
        }
        Schedule(image);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Schedule((Image)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var image = (Image)sender;
        var state = (State)image.GetValue(StateProperty);
        state.Version++;
        state.Pending?.Abort();
        state.Pending = null;
        SetSource(image, state, null);
    }

    private static void Schedule(Image image)
    {
        var state = (State)image.GetValue(StateProperty);
        var version = ++state.Version;
        state.Pending?.Abort();
        state.Pending = null;
        var path = GetPath(image);
        if (IconLoadQueue.Shared.TryGetCached(path, out var cached)) { SetSource(image, state, cached); return; }
        SetSource(image, state, null);
        state.Pending = image.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            state.Pending = null;
            if (state.Version != version) return;
            _ = ApplyAsync(new WeakReference<Image>(image), image.Dispatcher, version, IconLoadQueue.Shared.RequestAsync(path));
        });
    }

    private static async Task ApplyAsync(WeakReference<Image> target, Dispatcher dispatcher, int version, Task<BitmapSource?> loading)
    {
        var source = await loading.ConfigureAwait(false);
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (target.TryGetTarget(out var image) && image.GetValue(StateProperty) is State state && state.Version == version)
                    SetSource(image, state, source);
            }, DispatcherPriority.Background);
        }
        catch (TaskCanceledException) { /* Window/dispatcher closed while a batch was decoding. */ }
    }

    private static void SetSource(Image image, State state, BitmapSource? source)
    {
        state.Source = source;
        image.Source = source is not null && GetIsGrayscale(image)
            ? GrayscaleSources.GetValue(source, CreateGrayscale) : source;
    }

    private static BitmapSource CreateGrayscale(BitmapSource source)
    {
        var bitmap = source.Format == PixelFormats.Bgra32 ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var gray = (byte)((29 * pixels[i] + 150 * pixels[i + 1] + 77 * pixels[i + 2] + 128) >> 8);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = gray;
        }
        var grayscale = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        grayscale.Freeze();
        return grayscale;
    }
}
