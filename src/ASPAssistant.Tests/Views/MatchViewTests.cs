using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.App.Services;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class MatchViewTests
{
    [Theory]
    [InlineData(280)]
    [InlineData(400)]
    public void PreferenceCardsScrollIndependentlyAtSidebarWidths(int width)
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new MatchView { Width = width, Height = 390 };
            view.StrategyPicker.ItemsSource = Enumerable.Range(1, 61).Select(i => new Strategy { Name = $"策略{i}" }).ToArray();
            view.CovenantPicker.ItemsSource = Enumerable.Range(1, 26).Select(i => $"盟约{i}").ToArray();
            view.StrategySection.IsExpanded = view.CovenantSection.IsExpanded = true;
            LineupPerformanceTests.Layout(view);

            foreach (var picker in new[] { view.StrategyPicker, view.CovenantPicker })
            {
                var scroll = Assert.IsType<ScrollViewer>(picker.Template.FindName("PART_ScrollViewer", picker));
                Assert.True(scroll.ScrollableHeight > scroll.ViewportHeight);
                Assert.Equal(0, scroll.ScrollableWidth);
                var first = (ListBoxItem)picker.ItemContainerGenerator.ContainerFromIndex(0);
                var second = (ListBoxItem)picker.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.True(first.ActualHeight >= 56);
                Assert.True(first.ActualWidth >= 100);
                Assert.Equal(first.TranslatePoint(new Point(), picker).Y, second.TranslatePoint(new Point(), picker).Y);
                picker.SelectedIndex = 0;
                var pageOffset = view.PageScroll.VerticalOffset;
                scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                LineupPerformanceTests.Layout(view);
                Assert.True(scroll.VerticalOffset > 0);
                Assert.Equal(pageOffset, view.PageScroll.VerticalOffset);
                Assert.Equal(0, picker.SelectedIndex);
                scroll.ScrollToEnd();
                LineupPerformanceTests.Layout(view);
                Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset);
                view.PageScroll.ScrollToTop();
                LineupPerformanceTests.Layout(view);
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
                scroll.RaiseEvent(wheel);
                LineupPerformanceTests.Layout(view);
                Assert.True(wheel.Handled);
                Assert.True(view.PageScroll.VerticalOffset > 0);
            }
        });
    }

    [Fact]
    public void MatchRequiresProfileAndShowsRosterCountdownAndExitConfirmation()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var directory = Directory.CreateTempSubdirectory();
            var settings = new SettingsManager(directory.FullName);
            var server = new RoomHandler();
            using var site = new LineupSiteClient("", server);
            using var session = new MatchSession(site);
            var overlay = new OverlayWindow(settings);
            var view = new MatchView();
            try
            {
                settings.SaveMatchNickname("博士");
                string[] names = ["坚不可摧", "众志合一", "加急调派", "食腐之蝶", "统御号令"];
                string[] covenants = ["炎", "萨尔贡", "谢拉格", "乌萨斯", "维多利亚"];
                view.Configure(session, settings, names.Select(name => new Strategy { Name = name }).ToArray(),
                    new CovenantCatalog { CoreCovenants = [.. covenants] }, overlay);
                session.Changed += view.Refresh;
                Assert.Equal("博士", view.Nickname.Text);
                Assert.Empty(view.CovenantPicker.SelectedItems);
                Assert.Equal("随便", view.CovenantSummary.Text);
                Assert.Equal("随便", view.TacticPicker.SelectedItem);
                Assert.False(view.JoinButton.IsEnabled);
                view.StrategySection.IsExpanded = view.CovenantSection.IsExpanded = true;
                LineupPerformanceTests.Layout(view);
                view.StrategyPicker.SelectAll();
                Assert.Equal(4, view.StrategyPicker.SelectedItems.Count);
                Assert.Equal(string.Join("、", names.Take(4)), view.StrategySummary.Text);
                LineupPerformanceTests.Layout(view);
                var fifthStrategy = (ListBoxItem)view.StrategyPicker.ItemContainerGenerator.ContainerFromIndex(4);
                Assert.False(fifthStrategy.IsEnabled);
                view.StrategyPicker.SelectedItems.RemoveAt(0);
                LineupPerformanceTests.Layout(view);
                Assert.True(fifthStrategy.IsEnabled);
                view.StrategyPicker.SelectedItems.Add(view.StrategyPicker.Items[4]);
                Assert.Contains(names[4], view.StrategySummary.Text);
                view.CovenantPicker.SelectAll();
                Assert.Equal(4, view.CovenantPicker.SelectedItems.Count);
                Assert.Equal(string.Join("、", covenants.Take(4)), view.CovenantSummary.Text);
                Click(view.AnyCovenant);
                Assert.Empty(view.CovenantPicker.SelectedItems);
                Assert.Equal("随便", view.CovenantSummary.Text);
                Assert.True(view.JoinButton.IsEnabled);
                view.Nickname.Text = "小刻";
                Click(view.JoinButton);
                Assert.Equal("小刻", settings.LoadMatchNickname());
                Assert.Equal("正在核实匹配状态", view.Heading.Text);
                Assert.Equal(Visibility.Collapsed, view.JoinButton.Visibility);
                Assert.Equal(Visibility.Visible, view.LeaveButton.Visibility);
                session.RefreshAsync().GetAwaiter().GetResult();
                Assert.Equal("正在匹配", view.Heading.Text);
                Click(view.LeaveButton);
                Assert.Equal(Visibility.Visible, view.LeavePrompt.Visibility);
                Click(view.StayButton);
                Assert.Equal("queued", session.State?.Phase);
                Assert.Equal(0, server.LeaveRequests);

                server.Phase = "confirming";
                session.RefreshAsync().GetAwaiter().GetResult();
                Assert.Equal(4, view.Roster.Items.Count);
                Assert.Equal(Visibility.Visible, view.Countdown.Visibility);
                Assert.True(view.ShowConfirmationTakeover);
                Assert.True(view.ConfirmButton.IsEnabled);
                Click(view.ConfirmButton);
                Assert.False(view.ConfirmButton.IsEnabled);
                server.DeadlineElapsed = true;
                session.RefreshAsync().GetAwaiter().GetResult();
                Assert.False(view.ShowConfirmationTakeover);
                Assert.False(view.ConfirmButton.IsEnabled);
                Assert.Contains("确认时间已到", view.Description.Text);

                server.Phase = "joining";
                session.RefreshAsync().GetAwaiter().GetResult();
                Assert.Equal("游戏邀请", view.PublishedInvitation.Text);
                Assert.True(view.ReadyButton.IsEnabled);
                Assert.Equal(Visibility.Collapsed, view.ForceStartButton.Visibility);
                Click(view.ReadyButton);
                Assert.Equal("游戏进行中", view.Heading.Text);
                Click(view.MuteAllButton);
                Assert.False(view.MarkButton.IsEnabled);
                Click(view.MuteAllButton);
                Assert.True(view.MarkButton.IsEnabled);
                Click(view.MarkButton);
                Assert.Contains("游戏窗口", view.Feedback.Text);
                Click(view.LeaveButton);
                Click(view.ApproveLeaveButton);
                Assert.Equal(1, server.LeaveRequests);
                Assert.Equal(Visibility.Visible, view.JoinButton.Visibility);

                view.CustomTeam.IsChecked = true;
                Assert.Equal("创建或加入队伍", view.JoinButton.Content);
                Assert.Equal(Visibility.Visible, view.CreateRoom.Visibility);
                Assert.False(view.JoinButton.IsEnabled);
                view.InvitationDraft.Text = "卫戍协议房间 A";
                Assert.True(view.JoinButton.IsEnabled);
                Click(view.JoinButton);
                Assert.Equal("joining", session.State?.Phase);
                Assert.False(view.ShowConfirmationTakeover);
                Assert.Equal("等待队友加入房间", view.Heading.Text);
                Assert.Equal("队伍成员（1/4）", view.RosterTitle.Text);
                Assert.Equal(Visibility.Visible, view.CopyButton.Visibility);
                Assert.Equal(Visibility.Collapsed, view.ReadyButton.Visibility);
                Assert.Equal(Visibility.Visible, view.ForceStartButton.Visibility);
                Click(view.ForceStartButton);
                Assert.Equal("游戏进行中", view.Heading.Text);
                Assert.Contains("只有你一人", view.Description.Text);
                Assert.Equal(Visibility.Collapsed, view.ForceStartButton.Visibility);
            }
            finally
            {
                session.Changed -= view.Refresh;
                view.Finish();
                overlay.Close();
                directory.Delete(true);
            }
        });
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class RoomHandler : HttpMessageHandler
    {
        public string Phase = "queued";
        public int LeaveRequests;
        public bool DeadlineElapsed;
        private bool _confirmed, _muted, _lostJoin, _manual;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            switch (request.RequestUri!.Segments.Last())
            {
                case "join" when !_lostJoin:
                    _lostJoin = true;
                    throw new HttpRequestException("Lost the join response");
                case "confirm": _confirmed = true; break;
                case "join-room": _manual = true; Phase = "joining"; break;
                case "ready": Phase = "playing"; break;
                case "force-start" when _manual && Phase == "joining": Phase = "playing"; break;
                case "mute": _muted = !_muted; break;
                case "leave": LeaveRequests++; Phase = "ended"; break;
            }
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var members = Enumerable.Range(0, _manual ? 1 : 4).Select(i => new MatchMember($"p{i}", $"成员{i}", "坚不可摧",
                "随便", "兜底", "兜底", "#89DEEA", i != 0 || _confirmed, i != 0)).ToArray();
            var snapshot = new MatchSnapshot
            {
                Phase = Phase, SelfId = "p0", CaptainId = _manual ? "p0" : "p1", RoomId = "room", Deadline = DeadlineElapsed ? now - 1 : now + 20000,
                ServerNow = now, Invitation = Phase == "joining" ? "游戏邀请" : null, Members = members, CanForceStart = _manual && Phase == "joining",
                Muted = _muted ? ["p1", "p2", "p3"] : []
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(snapshot), Encoding.UTF8, "application/json")
            });
        }
    }
}
