using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public class IconLoadingTests
{
    [Fact]
    public async Task Queue_DecodesOffThreadInBoundedBatches_AndSharesDuplicateRequests()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decoded = 0;
        var callerThread = Environment.CurrentManagedThreadId;
        var firstDecodeThread = 0;
        using var queue = new IconLoadQueue(Path.GetTempPath(), path =>
        {
            if (Interlocked.Increment(ref decoded) == 1)
            {
                firstDecodeThread = Environment.CurrentManagedThreadId;
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            return Pixel(Colors.Cyan);
        }, async token => { paused.TrySetResult(); await resume.Task.WaitAsync(token); });
        var first = queue.RequestAsync("first.png");
        entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var requests = Enumerable.Range(1, 19).Select(i => queue.RequestAsync($"{i}.png")).Prepend(first).ToArray();
        queue.RequestAsync("first.png").Should().BeSameAs(first);
        release.Set();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Read(ref decoded).Should().Be(8, "the next batch must yield before decoding more icons");
        requests.Count(t => t.IsCompleted).Should().Be(8);
        firstDecodeThread.Should().NotBe(callerThread);
        resume.SetResult();
        var results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().OnlyContain(b => b != null && b.IsFrozen);
        decoded.Should().Be(20);
        (await queue.RequestAsync("first.png")).Should().BeSameAs(results[0]);
        decoded.Should().Be(20, "cached requests must not decode again");
    }

    [Fact]
    public async Task Queue_BadIconsDoNotStopLaterWork_AndPathsStayInsideCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "ASPA-IconTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "broken.png"), "broken");
            WritePng(Path.Combine(root, "valid.png"), Colors.Green);
            using var queue = new IconLoadQueue(root);
            (await queue.RequestAsync("../outside.png")).Should().BeNull();
            (await queue.RequestAsync("missing.png")).Should().BeNull();
            (await queue.RequestAsync("broken.png")).Should().BeNull();
            var valid = await queue.RequestAsync("valid.png");
            valid.Should().NotBeNull();
            valid!.IsFrozen.Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DeferredImage_DoesNotLoadInline_AndDiscardsStaleOrUnloadedResults()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var relative = "test-icons-" + Guid.NewGuid().ToString("N");
            var root = Path.Combine(AppContext.BaseDirectory, "data", relative);
            Directory.CreateDirectory(root);
            try
            {
                WritePng(Path.Combine(root, "red.png"), Colors.Red);
                WritePng(Path.Combine(root, "blue.png"), Colors.Blue);
                var image = new Image();
                DeferredIcon.SetPath(image, relative + "/red.png");
                image.Source.Should().BeNull("the binding must never read or decode a file inline");
                DeferredIcon.SetPath(image, relative + "/blue.png");
                PumpUntil(() => image.Source is BitmapSource);
                var pixels = new byte[4];
                ((BitmapSource)image.Source).CopyPixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
                pixels[0].Should().Be(255);
                pixels[2].Should().Be(0, "an old red result must not replace the current blue icon");
                image.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                image.Source.Should().BeNull();
                image.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                PumpUntil(() => image.Source != null);
                DeferredIcon.SetPath(image, "");
                image.Source.Should().BeNull("clearing an equipment slot also clears its thumbnail");
            }
            finally { Directory.Delete(root, true); }
        });
    }

    internal static void PumpUntil(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < timeout)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        predicate().Should().BeTrue("pending thumbnail work must finish without blocking the dispatcher");
    }

    private static BitmapSource Pixel(Color color)
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new[] { color.B, color.G, color.R, color.A }, 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void WritePng(string path, Color color)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Pixel(color)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
