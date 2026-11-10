using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class LineupDragPreviewTests
{
    [Fact]
    public void Follow_HasSmallLagWithoutOvershoot_AndIsIndependentOfFrameRate()
    {
        var start = new Point(20, 30);
        var target = new Point(100, 80);
        var first = LineupDragPreview.Follow(start, target, TimeSpan.FromMilliseconds(16));
        first.X.Should().BeInRange(21, 99);
        first.Y.Should().BeInRange(31, 79);
        var at30Fps = start;
        var at120Fps = start;
        for (var i = 0; i < 6; i++) at30Fps = LineupDragPreview.Follow(at30Fps, target, TimeSpan.FromSeconds(1d / 30));
        for (var i = 0; i < 24; i++) at120Fps = LineupDragPreview.Follow(at120Fps, target, TimeSpan.FromSeconds(1d / 120));
        (at30Fps - at120Fps).Length.Should().BeLessThan(0.001);
        (target - at30Fps).Length.Should().BeLessThan(2, "the image should settle quickly after the pointer stops");
        LineupDragPreview.Follow(start, target, TimeSpan.Zero).Should().Be(start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preview_UsesSharedIcon_FollowsPointerWithoutInterceptingDrops_AndCleansUp(bool equipment)
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
            var ops = store.LoadOperatorsAsync().GetAwaiter().GetResult();
            var (gear, _) = store.LoadEquipmentAsync().GetAwaiter().GetResult();
            var vm = new LineupEditorViewModel(ops, gear);
            var window = new LineupEditorWindow(vm, _ => Task.FromResult(true));
            try
            {
                var root = (FrameworkElement)window.Content;
                LineupPerformanceTests.Layout(root);
                var catalog = (LineupCatalogView)window.FindName("Catalog");
                var path = equipment ? gear[0].IconPath : ops[0].IconPath;
                var name = equipment ? gear[0].Name : ops[0].Name;
                var cached = IconLoadQueue.Shared.RequestAsync(path).GetAwaiter().GetResult();
                cached.Should().NotBeNull();
                Point? pointer = new Point(400, 250);
                using var preview = LineupDragPreview.Start(catalog, path, name, () => pointer);
                preview.Should().NotBeNull();
                var layer = AdornerLayer.GetAdornerLayer(preview!.AdornedElement);
                layer.GetAdorners(preview.AdornedElement).Should().Contain(preview);
                preview.IsHitTestVisible.Should().BeFalse();
                var card = (Border)VisualTreeHelper.GetChild(preview, 0);
                var image = ((Grid)card.Child).Children.OfType<Image>().Single();
                image.Source.Should().BeSameAs(cached, "dragging should reuse the decoded thumbnail");
                ((Grid)card.Child).Children.OfType<TextBlock>().Single().Visibility.Should().Be(Visibility.Collapsed,
                    "placeholder text must not show through a transparent equipment icon");
                var transform = (TranslateTransform)card.RenderTransform;
                var initial = transform.X;
                pointer = new Point(480, 250);
                preview.Advance(TimeSpan.FromMilliseconds(16));
                transform.X.Should().BeInRange(initial + 1, initial + 79);
                for (var frame = 0; frame < 20; frame++) preview.Advance(TimeSpan.FromMilliseconds(16));
                transform.X.Should().BeApproximately(initial + 80, 0.2);

                pointer = new Point(-20, 250);
                preview.Advance(TimeSpan.FromMilliseconds(16));
                card.Opacity.Should().Be(0, "the preview should disappear outside the editor");
                pointer = new Point(600, 250);
                preview.Advance(TimeSpan.FromMilliseconds(16));
                card.Opacity.Should().BeGreaterThan(0);
                transform.X.Should().BeApproximately(initial + 200, 0.01, "re-entering should not animate across the window");

                preview.Dispose();
                preview.Dispose();
                layer.GetAdorners(preview.AdornedElement).Should().BeNullOrEmpty();
                image.Source.Should().BeNull();
                pointer = new Point(800, 250);
                var stopped = transform.X;
                preview.Advance(TimeSpan.FromSeconds(1));
                transform.X.Should().Be(stopped, "a completed or cancelled drag must stop animating");

                using var next = LineupDragPreview.Start(catalog, path, name, () => pointer);
                root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                layer.GetAdorners(next!.AdornedElement).Should().BeNullOrEmpty("closing the editor must also clean up an active preview");
            }
            finally { vm.IsDirty = false; window.Close(); }
        });
    }
}
