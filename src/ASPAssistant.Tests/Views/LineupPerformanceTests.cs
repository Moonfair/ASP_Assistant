using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class LineupPerformanceTests
{
    [Fact]
    public void AddingOneMemberPreservesOtherCatalogContainers()
    {
        RunSta(() =>
        {
            var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
            var ops = store.LoadOperatorsAsync().GetAwaiter().GetResult();
            var (gear, _) = store.LoadEquipmentAsync().GetAwaiter().GetResult();
            var timings = new List<object>();
            var reused = true;
            var maxRealized = 0;
            for (var pass = 0; pass < 3; pass++)
            {
                var watch = Stopwatch.StartNew();
                var vm = new LineupEditorViewModel(ops, gear);
                var window = new LineupEditorWindow(vm, _ => Task.FromResult(true));
                var root = (FrameworkElement)window.Content;
                Layout(root);
                var openMs = watch.Elapsed.TotalMilliseconds;
                var catalog = (LineupCatalogView)window.FindName("Catalog");
                var list = (ListBox)catalog.FindName("OperatorList");
                maxRealized = Math.Max(maxRealized, Descendants<ListBoxItem>(list).Count());
                var survivor = vm.AvailableOperators[2];
                var container = list.ItemContainerGenerator.ContainerFromItem(survivor);
                watch.Restart();
                vm.AddOperator(vm.AvailableOperators[0]);
                Layout(root);
                var addMs = watch.Elapsed.TotalMilliseconds;
                reused &= ReferenceEquals(container, list.ItemContainerGenerator.ContainerFromItem(survivor));
                timings.Add(new { pass, openMs, addMs, loadedIcons = Descendants<Image>(root).Count(i => i.Source != null) });
                var last = vm.AvailableOperators.Last();
                list.ScrollIntoView(last);
                Layout(root);
                list.ItemContainerGenerator.ContainerFromItem(last).Should().NotBeNull("the last row must remain reachable");
                vm.OperatorBrowser.SearchText = survivor.Name;
                Layout(root);
                list.ItemContainerGenerator.ContainerFromItem(survivor).Should().NotBeNull("filtering after scrolling must clamp the offset and realize the result");
                vm.OperatorBrowser.SearchText = "no matching operator";
                Layout(root);
                Descendants<ListBoxItem>(list).Should().BeEmpty();
                vm.OperatorBrowser.SearchText = "";
                Layout(root);
                list.ItemContainerGenerator.ContainerFromItem(survivor).Should().NotBeNull();
                vm.IsDirty = false;
                window.Close();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
            var report = Environment.GetEnvironmentVariable("ASPA_PERF_REPORT");
            if (!string.IsNullOrEmpty(report))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                File.WriteAllText(report, JsonSerializer.Serialize(timings, new JsonSerializerOptions { WriteIndented = true }));
            }
            reused.Should().BeTrue("adding one member should not recreate every remaining avatar tile");
            maxRealized.Should().BeLessThan(40, "only visible rows and a small scrolling buffer should create controls");
        });
    }

    internal static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(1020, 730));
        element.Arrange(new Rect(0, 0, 1020, 730));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        element.UpdateLayout();
    }

    internal static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var matchChild in Descendants<T>(child)) yield return matchChild;
        }
    }
}
