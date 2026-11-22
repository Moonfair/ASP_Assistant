using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ASPAssistant.App.Services;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class MatchViewTests
{
    [Fact]
    public void PreferencesAreTheFirstEditableSectionWhenOpeningMatching()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new MatchView();
            var content = Assert.IsType<StackPanel>(view.PageScroll.Content);
            var firstSection = content.Children.Cast<UIElement>().First(section => section.Visibility == Visibility.Visible);
            Assert.Same(view.Preferences, firstSection);
        });
    }

    [Theory]
    [InlineData(280)]
    [InlineData(400)]
    public void OfferKeepsItsPageTitleAndCopyActionInViewWhilePreferencesScroll(int width)
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar();
            var view = sidebar.View;
            view.Width = width;
            view.Height = 390;
            sidebar.Observe(Idle);
            var title = Assert.IsType<TextBlock>(view.FindName("PageHeading"));
            Assert.NotEmpty(title.Text);
            Assert.Contains("策略", view.Description.Text);
            view.StrategyPicker.SelectedIndex = 0;
            Click(view.SearchButton);
            sidebar.Server.Snapshot = Offer("房主甲", "邀请 A");
            sidebar.Sync();

            view.StrategySection.IsExpanded = view.CovenantSection.IsExpanded = true;
            LineupPerformanceTests.Layout(view);
            var beforeScroll = view.CopyButton.TranslatePoint(new Point(), view);
            Assert.True(beforeScroll.Y >= view.PageScroll.TranslatePoint(new Point(), view).Y + view.PageScroll.ActualHeight);
            view.PageScroll.ScrollToEnd();
            LineupPerformanceTests.Layout(view);
            Assert.True(view.PageScroll.VerticalOffset > 0);
            Assert.Equal(beforeScroll, view.CopyButton.TranslatePoint(new Point(), view));
            Assert.True(beforeScroll.Y + view.CopyButton.ActualHeight <= view.ActualHeight);
            Assert.True(beforeScroll.X >= 0);
            Assert.True(view.CancelSearchButton.TranslatePoint(new Point(view.CancelSearchButton.ActualWidth, 0), view).X <= width);
            Assert.NotEqual(title.Text, view.Heading.Text);
            Assert.True(view.CopyButton.IsEnabled);
        });
    }

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
    public void PreferencesRestoreCatalogChoicesAndSaveEditsBeforeMatching()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar(new MatchPreferences(["旧策略", "统御号令", "坚不可摧"], ["旧盟约", "炎"], "兜底"));
            var view = sidebar.View;
            Assert.Equal("统御号令、坚不可摧", view.StrategySummary.Text);
            Assert.Equal("炎", view.CovenantSummary.Text);
            Assert.Equal("兜底", view.TacticPicker.SelectedItem);
            Assert.Contains("旧策略", sidebar.Settings.LoadMatchPreferences().Strategies);
            Assert.False(view.SearchButton.IsEnabled);

            sidebar.Observe(Idle with { Nickname = "" });
            Assert.False(view.SearchButton.IsEnabled);
            sidebar.Observe(Idle);
            Assert.Contains("博士", view.GameIdentity.Text);
            Assert.DoesNotContain(Self.Uid, view.GameIdentity.Text);
            Assert.True(view.SearchButton.IsEnabled);
            view.StrategyPicker.UnselectAll();
            Assert.False(view.SearchButton.IsEnabled);
            view.StrategySection.IsExpanded = view.CovenantSection.IsExpanded = true;
            view.StrategyPicker.SelectAll();
            view.CovenantPicker.SelectAll();
            LineupPerformanceTests.Layout(view);
            Assert.Equal(4, view.StrategyPicker.SelectedItems.Count);
            Assert.Equal(4, view.CovenantPicker.SelectedItems.Count);
            var fifth = (ListBoxItem)view.StrategyPicker.ItemContainerGenerator.ContainerFromIndex(4);
            Assert.False(fifth.IsEnabled);
            view.StrategyPicker.SelectedItems.RemoveAt(0);
            LineupPerformanceTests.Layout(view);
            Assert.True(fifth.IsEnabled);
            view.StrategyPicker.SelectedItems.Add(view.StrategyPicker.Items[4]);
            Click(view.AnyCovenant);
            view.TacticPicker.SelectedItem = "攻坚";
            var saved = sidebar.Settings.LoadMatchPreferences();
            Assert.Equal(new[] { "众志合一", "加急调派", "食腐之蝶", "统御号令" }, saved.Strategies);
            Assert.Empty(saved.Covenants);
            Assert.Equal("攻坚", saved.Tactic);
            Click(view.SearchButton);
            Assert.Equal(saved.Strategies, sidebar.Server.LastSync.GetProperty("strategies").Deserialize<string[]>());
        });
    }

    [Fact]
    public void SearchShowsOneOfferAtATimeUntilCancelledOrTheGameEntersARoom()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar();
            var view = sidebar.View;
            sidebar.Observe(Idle);
            view.StrategyPicker.SelectedIndex = 0;
            Click(view.SearchButton);
            Assert.Equal("searching", sidebar.Session.State?.Phase);
            Assert.Equal(Visibility.Visible, view.CancelSearchButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.LeaveButton.Visibility);
            sidebar.Server.Snapshot = Offer("房主甲", "邀请 A");
            sidebar.Sync();
            Assert.Equal("邀请 A", view.PublishedInvitation.Text);
            Assert.Contains("房主甲的房号", view.GameIdentity.Text);
            Assert.True(view.CopyButton.IsEnabled);
            Assert.Contains("房主甲 · 房主", RowText(view, 0));
            Assert.Contains("坚不可摧", RowText(view, 0));
            Assert.Equal(Visibility.Visible, view.Preferences.Visibility);
            view.TacticPicker.SelectedItem = "攻坚";
            Assert.Equal("攻坚", sidebar.Settings.LoadMatchPreferences().Tactic);

            sidebar.Server.NextOffer = Offer("房主乙", "邀请 B");
            Click(view.NextButton);
            Assert.Equal("邀请 B", view.PublishedInvitation.Text);
            Assert.Single(view.Roster.Items);
            Assert.Contains("房主乙 · 房主", RowText(view, 0));
            Assert.Equal(Visibility.Collapsed, view.LeaveButton.Visibility);
            Click(view.CancelSearchButton);
            Assert.Equal("idle", sidebar.Session.State?.Phase);
            Assert.Equal(Visibility.Visible, view.SearchButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.SharedInvitation.Visibility);

            Click(view.SearchButton);
            sidebar.Server.Snapshot = Offer("房主甲", "邀请 A");
            sidebar.Sync();
            sidebar.Observe(Room("lobby", Guest, Self, Guest), sync: false);
            Assert.Equal(Visibility.Collapsed, view.NextButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.SharedInvitation.Visibility);
            Assert.Equal(2, view.Roster.Items.Count);
            Assert.Contains("同房队友 · 房主", RowText(view, 1));
            Assert.Equal(3, RowText(view, 1).Count(text => text == "未知"));
            Assert.Equal(Visibility.Collapsed, view.InviteEditor.Visibility);
        });
    }

    [Fact]
    public void AnOfferWithAnInterruptedConnectionHidesTheInvitationUntilReconfirmed()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar();
            var view = sidebar.View;
            sidebar.Observe(Idle);
            view.StrategyPicker.SelectedIndex = 0;
            Click(view.SearchButton);
            sidebar.Server.Snapshot = Offer("房主甲", "邀请 A");
            sidebar.Sync();
            Assert.True(view.CopyButton.IsEnabled);

            sidebar.Server.Offline = true;
            sidebar.Sync();
            Assert.False(view.CopyButton.IsEnabled);
            Assert.Equal(Visibility.Collapsed, view.SharedInvitation.Visibility);
            Assert.Empty(view.PublishedInvitation.Text);
            Assert.False(view.NextButton.IsEnabled);
            Assert.True(view.CancelSearchButton.IsEnabled);
            sidebar.Server.Offline = false;
            sidebar.Sync();
            Assert.Equal("邀请 A", view.PublishedInvitation.Text);
            Assert.True(view.CopyButton.IsEnabled);
        });
    }

    [Fact]
    public void HostRecruitsWithRoomInvitationAndKeepsTheDraftAcrossPollsAndReaderGaps()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar();
            var view = sidebar.View;
            var room = Room("lobby", Self, Self, Guest);
            sidebar.Server.Snapshot = Mirror(room);
            sidebar.Observe(room);
            Assert.Contains(room.RoomId!, view.GameIdentity.Text);
            Assert.Contains("博士（我） · 房主", RowText(view, 0));
            Assert.Equal(3, RowText(view, 1).Count(text => text == "未知"));
            Assert.Equal(Visibility.Visible, view.InviteEditor.Visibility);
            Assert.False(view.RecruitButton.IsEnabled);
            view.InvitationDraft.Text = "游戏房间的分享文本";
            Assert.False(view.RecruitButton.IsEnabled);
            view.StrategyPicker.SelectedIndex = 0;
            Assert.True(view.RecruitButton.IsEnabled);
            var rows = view.Roster.ItemsSource;
            sidebar.Sync();
            Assert.Same(rows, view.Roster.ItemsSource);
            Assert.Equal("游戏房间的分享文本", view.InvitationDraft.Text);

            sidebar.Session.Observe(new BattleUpdate(BattleFeedState.ReadInterrupted));
            Assert.Empty(view.Roster.Items);
            Assert.False(view.SearchButton.IsEnabled);
            sidebar.Observe(room);
            Assert.Equal("游戏房间的分享文本", view.InvitationDraft.Text);
            Click(view.RecruitButton);
            Assert.Equal("游戏房间的分享文本", view.PublishedInvitation.Text);
            Assert.Equal(Visibility.Visible, view.StopRecruitButton.Visibility);
            Click(view.StopRecruitButton);
            Assert.Equal(Visibility.Visible, view.InviteEditor.Visibility);
            Assert.Equal("游戏房间的分享文本", view.InvitationDraft.Text);
            Click(view.RecruitButton);

            var full = Room("lobby", Self, Self, Guest, Peer, new GameRoomSeat("100004", "第四人", false));
            sidebar.Observe(full, sync: false);
            Assert.Equal(4, view.Roster.Items.Count);
            Assert.Equal(Visibility.Collapsed, view.StopRecruitButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.RecruitButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.SharedInvitation.Visibility);
            var nativeQueue = Room("matching", Self, Self, Guest);
            sidebar.Server.Snapshot = Mirror(nativeQueue);
            sidebar.Observe(nativeQueue);
            Assert.Equal("游戏内匹配中", view.Heading.Text);
            Assert.Equal(Visibility.Collapsed, view.RecruitButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.ForceStartButton.Visibility);

            sidebar.Observe(Idle with { Phase = "matching" }, sync: false);
            Assert.Equal("游戏内匹配中", view.Heading.Text);
            Assert.Equal(Visibility.Collapsed, view.SearchButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.LeaveButton.Visibility);
            var next = room with { RoomId = "room-B", HostUid = Guest.Uid };
            sidebar.Server.Snapshot = Mirror(next) with { CanForceStart = true };
            sidebar.Observe(next);
            Assert.Contains("room-B", view.GameIdentity.Text);
            Assert.Empty(view.InvitationDraft.Text);
            Assert.Equal(Visibility.Collapsed, view.InviteEditor.Visibility);
            Assert.Equal(Visibility.Collapsed, view.ForceStartButton.Visibility);

            var recruiting = room with { RoomId = "room-C" };
            sidebar.Server.Snapshot = Mirror(recruiting) with { Recruiting = true, Invitation = "当前发布的邀请文本" };
            sidebar.Observe(recruiting);
            Click(view.StopRecruitButton);
            Assert.Equal("当前发布的邀请文本", view.InvitationDraft.Text);
        });
    }

    [Fact]
    public void PlayingShowsOnlyPluginMuteTargetsAndConfirmsLeavingTheObservedRoom()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar();
            var view = sidebar.View;
            var room = Room("lobby", Self, Self, Peer, Guest);
            sidebar.Server.Snapshot = Mirror(room) with { CanForceStart = true };
            sidebar.Observe(room);
            Assert.Equal(Visibility.Visible, view.ForceStartButton.Visibility);
            Click(view.ForceStartButton);
            Assert.Equal("游戏进行中", view.Heading.Text);
            Assert.Equal(Visibility.Collapsed, view.ForceStartButton.Visibility);
            Assert.Equal(Visibility.Visible, view.Preferences.Visibility);
            view.StrategyPicker.SelectedIndex = 1;
            Assert.Contains("众志合一", sidebar.Settings.LoadMatchPreferences().Strategies);
            Assert.Equal(3, RowText(view, 2).Count(text => text == "未知"));
            var guestButtons = RowChildren<Button>(view, 2);
            Assert.DoesNotContain(guestButtons, b => b.Visibility == Visibility.Visible);
            var mutePeer = Assert.Single(RowChildren<Button>(view, 1), b => b.Visibility == Visibility.Visible);
            Click(mutePeer);
            Assert.Equal(new[] { PublicId(Peer) }, sidebar.Session.State!.Muted);
            Assert.False(view.MarkButton.IsEnabled);
            Click(view.MuteAllButton);
            Assert.Empty(sidebar.Session.State.Muted);
            Assert.True(view.MarkButton.IsEnabled);
            Click(view.MuteAllButton);
            Assert.Equal(new[] { PublicId(Peer) }, sidebar.Session.State.Muted);

            Click(view.LeaveButton);
            Assert.Equal(Visibility.Visible, view.LeavePrompt.Visibility);
            Click(view.StayButton);
            Assert.Equal(0, sidebar.Server.LeaveRequests);
            Click(view.LeaveButton);
            Click(view.ApproveLeaveButton);
            Assert.Equal(1, sidebar.Server.LeaveRequests);
            Assert.Empty(view.Roster.Items);
            Assert.Equal(Visibility.Collapsed, view.SearchButton.Visibility);
            sidebar.Observe(room);
            Assert.Empty(view.Roster.Items);
            sidebar.Observe(Idle);
            Assert.True(view.SearchButton.IsEnabled);
        });
    }

    private static readonly GameRoomSeat Self = new("100001", "博士", true);
    private static readonly GameRoomSeat Guest = new("100002", "同房队友", false);
    private static readonly GameRoomSeat Peer = new("100003", "插件队友", true);
    private static readonly GameRoomObservation Idle = new(Self.Uid, Self.Nickname, null, null, "idle", []);

    private static GameRoomObservation Room(string phase, GameRoomSeat host, params GameRoomSeat[] seats) =>
        new(Self.Uid, Self.Nickname, "room-A", host.Uid, phase, seats);

    private static string PublicId(GameRoomSeat seat) => $"participant-{seat.Uid}";

    private static MatchMember Member(GameRoomSeat seat) =>
        new(PublicId(seat), seat.Nickname, "坚不可摧", "炎", "兜底", "兜底", "#89DEEA", true, seat.Ready, seat != Guest);

    private static MatchSnapshot Mirror(GameRoomObservation game) => new()
    {
        Phase = game.Phase, RoomId = $"mirror-{game.RoomId}", GameRoomId = game.RoomId, SelfId = PublicId(Self),
        CaptainId = $"participant-{game.HostUid}", Members = game.Members.Select(Member).ToArray()
    };

    private static MatchSnapshot Offer(string host, string invitation)
    {
        var seat = new GameRoomSeat(host, host, false);
        return new MatchSnapshot
        {
            Phase = "offer", RoomId = $"offer-{host}", GameRoomId = $"{host}的房号", SelfId = PublicId(Self), CaptainId = PublicId(seat),
            Members = [Member(seat)], Invitation = invitation, Recruiting = true
        };
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string[] RowText(MatchView view, int index) =>
        RowChildren<TextBlock>(view, index).Select(block => block.Text).ToArray();

    private static T[] RowChildren<T>(MatchView view, int index) where T : DependencyObject
    {
        LineupPerformanceTests.Layout(view);
        var root = view.Roster.ItemContainerGenerator.ContainerFromIndex(index);
        return Descendants<T>(root).ToArray();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class Sidebar : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();
        private readonly LineupSiteClient _site;
        private readonly OverlayWindow _overlay;
        internal RoomServer Server { get; } = new();
        internal SettingsManager Settings { get; }
        internal MatchSession Session { get; }
        internal MatchView View { get; } = new() { Width = 300, Height = 520 };

        internal Sidebar(MatchPreferences? saved = null)
        {
            _site = new LineupSiteClient("", Server);
            Session = new MatchSession(_site);
            Settings = new SettingsManager(_directory.FullName);
            if (saved != null) Settings.SaveMatchPreferences(saved);
            _overlay = new OverlayWindow(Settings);
            string[] names = ["坚不可摧", "众志合一", "加急调派", "食腐之蝶", "统御号令"];
            View.Configure(Session, Settings, names.Select(name => new Strategy { Name = name }).ToArray(),
                new CovenantCatalog { CoreCovenants = ["炎", "萨尔贡", "谢拉格", "乌萨斯", "维多利亚"] }, _overlay);
            Session.Changed += View.Refresh;
        }

        internal void Observe(GameRoomObservation observation, bool sync = true)
        {
            Session.Observe(new BattleUpdate(BattleFeedState.WaitingForRound, Room: observation));
            if (sync) Sync();
        }

        internal void Sync() => Session.RefreshAsync().GetAwaiter().GetResult();

        public void Dispose()
        {
            Session.Changed -= View.Refresh;
            Session.Dispose();
            View.Finish();
            _overlay.Close();
            _site.Dispose();
            _directory.Delete(true);
        }
    }

    private sealed class RoomServer : HttpMessageHandler
    {
        internal MatchSnapshot Snapshot = new() { Phase = "idle", SelfId = PublicId(Self) };
        internal MatchSnapshot? NextOffer;
        internal JsonElement LastSync;
        internal int LeaveRequests;
        internal bool Offline;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("offline");
            var body = JsonSerializer.Deserialize<JsonElement>(request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            switch (request.RequestUri!.Segments.Last())
            {
                case "sync": LastSync = body; break;
                case "search": Snapshot = Snapshot with { Phase = "searching" }; break;
                case "next": Snapshot = NextOffer!; break;
                case "cancel-search": Snapshot = new MatchSnapshot { Phase = "idle", SelfId = PublicId(Self) }; break;
                case "recruit": Snapshot = Snapshot with { Recruiting = true, Invitation = body.GetProperty("invitation").GetString() }; break;
                case "stop-recruit": Snapshot = Snapshot with { Recruiting = false, Invitation = null }; break;
                case "force-start": Snapshot = Snapshot with { Phase = "playing", CanForceStart = false }; break;
                case "mute": Snapshot = Snapshot with { Muted = body.GetProperty("muted").Deserialize<string[]>()! }; break;
                case "leave": LeaveRequests++; break;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Snapshot)
            });
        }
    }
}
