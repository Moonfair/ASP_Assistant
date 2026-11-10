using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App.Services;

namespace ASPAssistant.App.Controls;

/// <summary>Defers uncached file work until after layout and applies results below input/render priority.</summary>
public static class DeferredIcon
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached(
        "Path", typeof(string), typeof(DeferredIcon), new PropertyMetadata("", OnPathChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(DeferredIcon));
    private sealed class State
    {
        public int Version;
        public DispatcherOperation? Pending;
    }

    public static string GetPath(DependencyObject target) => (string)target.GetValue(PathProperty);
    public static void SetPath(DependencyObject target, string value) => target.SetValue(PathProperty, value);

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
        image.Source = null;
    }

    private static void Schedule(Image image)
    {
        var state = (State)image.GetValue(StateProperty);
        var version = ++state.Version;
        state.Pending?.Abort();
        state.Pending = null;
        var path = GetPath(image);
        image.Source = null;
        if (IconLoadQueue.Shared.TryGetCached(path, out var cached)) { image.Source = cached; return; }
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
                    image.Source = source;
            }, DispatcherPriority.Background);
        }
        catch (TaskCanceledException) { /* Window/dispatcher closed while a batch was decoding. */ }
    }
}
