using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class BetaUpdateInteraction
{
    // Runs inside the suite's shared STA/Application with the real window and a local HTTP fixture.
    public static void Verify()
    {
        var window = new SidePanelWindow(new(), new(), new(), new(), new())
        {
            Left = -32000, Top = -32000, Height = 620, ShowActivated = false, ShowInTaskbar = false, Topmost = false
        };
        var settings = (FrameworkElement)window.FindName("SettingsPage");
        var stable = (Button)window.FindName("SettingsCheckUpdateButton");
        var beta = window.FindName("SettingsCheckBetaButton") as Button;
        beta.Should().NotBeNull("only settings should offer a one-time Beta check");
        settings.IsAncestorOf(beta!).Should().BeTrue();

        using var handler = new Releases();
        using var http = new HttpClient(handler);
        window.StartUpdateCheck(new UpdateService(http, "3.8.11"));
        Click(stable);
        Until(() => stable.IsEnabled);
        var banner = (TextBlock)window.FindName("UpdateBannerText");
        banner.Text.Should().Contain("v3.8.12");
        var stableBanner = banner.Text;

        window.Show();
        Click((Button)window.FindName("SettingsButton"));
        Until(() => beta!.IsEnabled);
        Click(beta!);
        stable.IsEnabled.Should().BeFalse();
        beta!.IsEnabled.Should().BeFalse();
        bool previewed = false;
        void DeferPreview(object? sender, EventArgs args) =>
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                // Capture rendered state, not a constructor side effect. Do not download or install.
                var dialog = window.OwnedWindows.OfType<UpdateDialog>().Single();
                previewed = ((FrameworkElement)dialog.FindName("BetaWarning")).IsVisible
                    && ((Button)dialog.FindName("UpdateButton")).Content.ToString()!.Contains("Beta");
                Click((Button)dialog.FindName("DeferButton"));
            }));

        ComponentDispatcher.EnterThreadModal += DeferPreview;
        try
        {
            handler.Beta.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                { tag = "v3.9.0-beta.1", notes = "测试说明", sha256 = new string('a', 64) }) });
            Until(() => stable.IsEnabled && beta.IsEnabled);
        }
        finally
        {
            ComponentDispatcher.EnterThreadModal -= DeferPreview;
        }
        previewed.Should().BeTrue("a manual check must show a distinct Beta confirmation before installation");
        banner.Text.Should().Be(stableBanner, "a declined Beta must not replace the ordinary update");
        var toggle = (Button)window.FindName("SettingsButton");
        Click(toggle); // Close and reopen with a newer stable than both installed and Beta.
        handler.StableTag = "v3.10.0";
        Click(toggle);
        Until(() => !beta.IsEnabled && beta.ToolTip?.ToString()?.Contains("正式版") == true);
        beta.Opacity.Should().BeLessThan(1);
        stable.IsEnabled.Should().BeTrue();
        var requests = handler.Paths.Count;
        Click(beta); // Routed events can be raised even on a disabled WPF button.
        handler.Paths.Should().HaveCount(requests);
        window.OwnedWindows.Cast<Window>().Should().BeEmpty();

        Click(toggle);
        handler.BetaTag = "v3.10.1-beta.1";
        Click(toggle);
        Until(() => beta.IsEnabled);
        window.OwnedWindows.Cast<Window>().Should().BeEmpty("opening settings only checks availability");

        handler.StableTag = "v3.10.1"; // A final may ship while the settings page remains open.
        Click(beta);
        Until(() => stable.IsEnabled && !beta.IsEnabled);
        window.OwnedWindows.Cast<Window>().Should().BeEmpty();
        handler.Paths.Should().OnlyContain(path => path.EndsWith("/latest") || path.EndsWith("/latest.json"),
            "metadata checks must not download installers");
        Click(toggle);
        window.Hide();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Until(Func<bool> done)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        done().Should().BeTrue();
    }

    private sealed class Releases : HttpMessageHandler
    {
        public string StableTag = "v3.8.12", BetaTag = "v3.9.0-beta.1";
        private int _betaReads;
        public readonly List<string> Paths = [];
        public readonly TaskCompletionSource<HttpResponseMessage> Beta = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == "/updates/beta/latest.json")
            {
                if (++_betaReads == 2) return Beta.Task.WaitAsync(token); // delay explicit preview, not availability
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                    { tag = BetaTag, sha256 = new string('a', 64) }) });
            }
            if (path != "/repos/Moonfair/ASP_Assistant/releases/latest") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                tag_name = StableTag, assets = new[] { new { name = $"ASPAssistant-{StableTag}-win-x64-setup.exe",
                    browser_download_url = "https://example.invalid/stable" } }
            }) });
        }
    }
}
