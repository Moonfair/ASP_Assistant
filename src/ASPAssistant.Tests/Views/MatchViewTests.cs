using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class MatchViewTests
{
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
                view.Configure(session, settings, [new Strategy { Name = "坚不可摧" }],
                    new CovenantCatalog { CoreCovenants = ["炎"] }, overlay);
                session.Changed += view.Refresh;
                Assert.Equal("博士", view.Nickname.Text);
                Assert.Equal("随便", view.CovenantPicker.SelectedItem);
                Assert.Equal("随便", view.TacticPicker.SelectedItem);
                Assert.False(view.JoinButton.IsEnabled);
                view.StrategyPicker.SelectedIndex = 0;
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
        private bool _confirmed, _muted, _lostJoin;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            switch (request.RequestUri!.Segments.Last())
            {
                case "join" when !_lostJoin:
                    _lostJoin = true;
                    throw new HttpRequestException("Lost the join response");
                case "confirm": _confirmed = true; break;
                case "ready": Phase = "playing"; break;
                case "mute": _muted = !_muted; break;
                case "leave": LeaveRequests++; Phase = "ended"; break;
            }
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var members = Enumerable.Range(0, 4).Select(i => new MatchMember($"p{i}", $"成员{i}", "坚不可摧",
                "随便", "兜底", "兜底", "#89DEEA", i != 0 || _confirmed, i != 0)).ToArray();
            var snapshot = new MatchSnapshot
            {
                Phase = Phase, SelfId = "p0", CaptainId = "p1", RoomId = "room", Deadline = DeadlineElapsed ? now - 1 : now + 20000,
                ServerNow = now, Invitation = Phase == "joining" ? "游戏邀请" : null, Members = members,
                Muted = _muted ? ["p1", "p2", "p3"] : []
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(snapshot), Encoding.UTF8, "application/json")
            });
        }
    }
}
