using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class CatalogDetailsTests
{
    [Fact]
    public async Task CatalogDetails_LoadsRealSkillNumbersAndSeasonTips()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var details = await store.LoadCatalogDetailsAsync();
        details.Season.Should().Be("act3autochess");
        details.Skills["黑键"].Select(skill => skill.Slot).Should().Equal(1, 2, 3);
        details.Skills["黑键"][0].Description.Should().Contain("攻击力50%");
        details.Skills["黑键"][0].Timing.Should().Contain("技力 15 · 初始 5");
        details.Skills["黑键"][2].Description.Should().Contain("\n").And.NotContain(@"\n");
        details.Skills["风丸"].Should().HaveCount(2);
        details.Skills.Values.SelectMany(skills => skills).Should().OnlyContain(skill => !skill.Description.Contains('{'));
        details.Tips!.Operators["旅骨"].Seconds.Should().Be(5);
        details.Tips.Equipment.Should().ContainKey("雪祀之衣");
    }

    [Fact]
    public async Task CatalogWindow_ShowsEffectsSkillsAndPublicResultsOnDemand()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var details = await store.LoadCatalogDetailsAsync();
        var op = (await store.LoadOperatorsAsync()).Single(item => item.Name == "黑键");
        var (equipment, _) = await store.LoadEquipmentAsync();

        LineupPerformanceTests.RunSta(() =>
        {
            var handler = new VoteHandler();
            using var site = new LineupSiteClient(Path.GetTempPath(), handler);
            var window = new CatalogDetailWindow(op, details, equipment, site);
            try
            {
                ((TextBlock)window.FindName("TitleText")).Text.Should().Be(op.Name);
                ((WrapPanel)window.FindName("Tags")).Children.Count.Should().BeGreaterThan(0);
                ((TextBlock)window.FindName("TipText")).Text.Should().Contain("二技能");
                ((StackPanel)window.FindName("Effects")).Children.Count.Should().Be(2);
                ((ItemsControl)window.FindName("SkillDetails")).Items.Count.Should().Be(3);
                handler.Count.Should().Be(0);
                var votesTab = (TabItem)window.FindName("VotesTab");
                ((TabControl)window.FindName("Sections")).SelectedItem = votesTab;
                var results = (StackPanel)window.FindName("SkillResults");
                IconLoadingTests.PumpUntil(() => results.Children.Count == 3);
                ((TextBlock)results.Children[1]).Text.Should().EndWith("9 票");
                ((StackPanel)window.FindName("SignInPanel")).Visibility.Should().Be(Visibility.Visible);
                ((StackPanel)window.FindName("EquipmentResults")).Children.Count.Should().Be(1);
                site.GetOperatorVotesAsync(details.Season!, op.Name).IsCompletedSuccessfully.Should().BeTrue();
                handler.Count.Should().Be(1);
            }
            finally { window.Close(); }

            var item = equipment.Single(gear => gear.Name == "雪祀之衣");
            var gearWindow = new CatalogDetailWindow(item, details);
            try
            {
                ((TextBlock)gearWindow.FindName("TipText")).Text.Should().Contain("森蚺");
                ((StackPanel)gearWindow.FindName("Effects")).Children.Count.Should().BeGreaterThan(0);
                ((TabItem)gearWindow.FindName("VotesTab")).Visibility.Should().Be(Visibility.Collapsed);
            }
            finally { gearWindow.Close(); }
        });
    }

    [Fact]
    public async Task CatalogWindow_RecordsSkillAndEquipmentSeparatelyAndShowsThanks()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var details = await store.LoadCatalogDetailsAsync();
        var op = (await store.LoadOperatorsAsync()).Single(item => item.Name == "黑键");
        var (equipment, _) = await store.LoadEquipmentAsync();
        var settings = Path.Combine(Path.GetTempPath(), "ASPA-Vote-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new SignedInVoteHandler();
            using var site = new LineupSiteClient(settings, handler);
            await site.SignInAsync("login", "000000", CancellationToken.None);

            LineupPerformanceTests.RunSta(() =>
            {
                var window = new CatalogDetailWindow(op, details, equipment, site);
                try
                {
                    ((TabControl)window.FindName("Sections")).SelectedItem = window.FindName("VotesTab");
                    var skillForm = (StackPanel)window.FindName("SkillVoteForm");
                    IconLoadingTests.PumpUntil(() => skillForm.Visibility == Visibility.Visible);
                    var skillChoices = (WrapPanel)window.FindName("SkillChoices");
                    ((RadioButton)skillChoices.Children[1]).IsChecked = true;
                    var skillSubmit = (Button)window.FindName("SkillSubmit");
                    skillSubmit.IsEnabled.Should().BeTrue();
                    skillSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    IconLoadingTests.PumpUntil(() => skillForm.Visibility == Visibility.Collapsed);
                    ((TextBlock)window.FindName("SkillFeedback")).Text.Should().Contain("感谢");

                    var picker = (Expander)window.FindName("EquipmentPicker");
                    picker.IsExpanded = true;
                    var search = (TextBox)window.FindName("EquipmentSearch");
                    search.Text = "雪祀";
                    var list = (ListBox)window.FindName("EquipmentChoices");
                    list.Items.Count.Should().Be(1);
                    LineupPerformanceTests.Layout((FrameworkElement)window.Content);
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    FindCheckBox(row)!.IsChecked = true;
                    search.Text = "信标";
                    LineupPerformanceTests.Layout((FrameworkElement)window.Content);
                    row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    FindCheckBox(row)!.IsChecked = true;

                    var gearSubmit = (Button)window.FindName("EquipmentSubmit");
                    gearSubmit.Content.Should().Be("提交装备票（2 件）");
                    gearSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    IconLoadingTests.PumpUntil(() =>
                        ((StackPanel)window.FindName("EquipmentVoteForm")).Visibility == Visibility.Collapsed);
                    ((TextBlock)window.FindName("EquipmentFeedback")).Text.Should().Contain("感谢");
                    handler.Ballots.Should().HaveCount(2);
                    handler.Ballots[0].GetProperty("skill").GetInt32().Should().Be(2);
                    handler.Ballots[1].GetProperty("equipment").EnumerateArray().Select(value => value.GetString())
                        .Should().BeEquivalentTo("雪祀之衣", "信标");
                }
                finally { window.Close(); }
            });
        }
        finally
        {
            if (Directory.Exists(settings)) Directory.Delete(settings, true);
        }
    }

    private static CheckBox? FindCheckBox(DependencyObject element)
    {
        if (element is CheckBox box) return box;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindCheckBox(VisualTreeHelper.GetChild(element, i)) is { } child) return child;
        return null;
    }

    private sealed class SignedInVoteHandler : HttpMessageHandler
    {
        public List<JsonElement> Ballots { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/desktop/verify")
                return Json(new
                {
                    user = new { email = "vote@example.test" },
                    token = new string('a', 64),
                    expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
                });

            if (path.EndsWith("/mine") || request.Method == HttpMethod.Post)
                request.Headers.Authorization?.Parameter.Should().Be(new string('a', 64));
            if (path.EndsWith("/mine")) return Json(new { skillVoted = false, equipmentVoted = false });
            if (request.Method == HttpMethod.Get) return Json(new { skills = Array.Empty<int>(), equipment = Array.Empty<int>() });
            Ballots.Add(await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken));
            return Json(new { recorded = true });
        }

        private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
    }

    private sealed class VoteHandler : HttpMessageHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.Should().Be("/api/operator-votes");
            request.Headers.Authorization.Should().BeNull();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"skills":[{"choice":2,"votes":9}],"equipment":[{"choice":"雪祀之衣","votes":4}]}
                    """)
            });
        }
    }
}
