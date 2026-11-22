using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.App.Services;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public partial class BrowseFilterInteractionTests
{
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Offline UI previews must not send network requests.");
    }

    private static void VerifySecondarySidebarPages()
    {
        var window = new SidePanelWindow(new(), new(), new(), new(), new()) { Height = 680, UseSideFilters = false };
        var tabs = (TabControl)window.FindName("MainTabs");
        var pages = tabs.Items.Cast<TabItem>().Skip(3).ToArray();
        var preview = Environment.GetEnvironmentVariable("ASPA_SECONDARY_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(preview)) RenderSecondarySamples(preview);

        foreach (int width in new[] { 280, 310, 400 })
        {
            window.Width = width;
            window.Height = width == 280 ? 420 : 680;
            foreach (var page in pages)
            {
                tabs.SelectedItem = page;
                LayoutFilterWindow(window);
                var view = (UserControl)page.Content;
                view.ActualWidth.Should().BeApproximately(tabs.ActualWidth, 1);
                var title = view.FindName("PageHeading") as TextBlock;
                title.Should().NotBeNull($"the {page.Header} page needs a persistent heading");
                title!.Visibility.Should().Be(Visibility.Visible);
                title.Text.Should().NotBeNullOrWhiteSpace();
                VisualChildren<ScrollViewer>(view).Where(scroll => scroll.ActualWidth > 0)
                    .Should().OnlyContain(scroll => scroll.ScrollableWidth < 1, "sidebar pages must not require horizontal scrolling");
            }
        }

        void RenderSecondarySamples(string directory)
        {
            Directory.CreateDirectory(directory);
            var enemy = (EnemyView)pages[0].Content;
            enemy.UpdateInfo(new("假想敌：铳", ["致命", "复苏", "折射"], 11, "终极模拟"));
            var lineups = (LineupView)pages[1].Content;
            var catalog = (ItemsControl)lineups.FindName("LineupCards");
            catalog.ItemsSource = new[] { new Lineup {
                Name = "莱塔尼亚叠层阵容", Author = "演示", CovenantTags = ["莱塔尼亚"], AvailableStrategies = ["统御号令"],
                Members = [new() { OperatorName = "地灵" }, new() { OperatorName = "折光" }, new() { OperatorName = "褐果" }]
            } };
            var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
            var strategies = store.LoadStrategiesAsync().GetAwaiter().GetResult();
            var strategyIcons = strategies.ToDictionary(item => item.Name, item => item.IconPath);
            lineups.ConfigurePortraits(name => $"icons/operators/{name}.png", _ => false, name => strategyIcons.GetValueOrDefault(name));
            foreach (var name in new[] { "地灵", "折光", "褐果" })
                IconLoadQueue.Shared.RequestAsync($"icons/operators/{name}.png").GetAwaiter().GetResult();
            var data = (BattleDataView)pages[3].Content;
            data.SetNames(new Dictionary<string, string> { ["a"] = "地灵", ["b"] = "折光", ["c"] = "褐果" });
            data.Present(new(BattleFeedState.Live, new(14, 750, 25, false, new()
                { ["a"] = new(62400, 0, 0, 0), ["b"] = new(31800, 0, 0, 0), ["c"] = new(5800, 0, 0, 0) })));
            string[] ids = ["enemy", "lineup", "match", "data", "debug"];
            foreach (int width in new[] { 280, 310, 400 })
            {
                window.Width = width;
                for (int index = 0; index < pages.Length; ++index)
                {
                    tabs.SelectedItem = pages[index];
                    LayoutFilterWindow(window);
                    Render($"{ids[index]}-{width}.png");
                }
            }

            // Extra states use the real view methods, a private settings directory and an offline HTTP handler.
            var sampleSettings = Path.Combine(directory, "settings-fixture");
            Directory.CreateDirectory(sampleSettings);
            using var site = new LineupSiteClient(sampleSettings, new NoNetwork());
            using var session = new MatchSession(site);
            var settings = new SettingsManager(sampleSettings);
            var overlay = new OverlayWindow(settings);
            var match = (MatchView)pages[2].Content;
            try
            {
                match.Configure(session, settings, strategies.Take(4).ToArray(), new CovenantCatalog { CoreCovenants = ["炎", "维多利亚"] }, overlay);
                tabs.SelectedItem = pages[2];
                window.Width = 280;
                LayoutFilterWindow(window);
                Render("match-configured-280.png");
            }
            finally { match.Finish(); overlay.Close(); }
            tabs.SelectedItem = pages[0];
            LayoutFilterWindow(window);
            if (enemy.FindName("RoundPicker") is ListBox selector)
            {
                selector.SelectedIndex = 1;
                LayoutFilterWindow(window);
                Render("enemy-15-280.png");
            }

            void Render(string fileName)
            {
                var bitmap = new RenderTargetBitmap((int)window.Width * 2, (int)window.Height * 2, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render((FrameworkElement)window.Content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, fileName));
                encoder.Save(file);
            }
        }
    }
}
