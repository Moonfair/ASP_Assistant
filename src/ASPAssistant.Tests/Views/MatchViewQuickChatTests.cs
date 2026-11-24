using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;
using ASPAssistant.App.Services;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Data;

namespace ASPAssistant.Tests.Views;

public partial class MatchViewTests
{
    [Fact]
    public void QuickChatIsOnlyVisibleInThePlayingRoom()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar();
            var panel = sidebar.View.ShoutPanel;
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            var room = Room("playing", Self, Self, Peer);
            sidebar.Server.Snapshot = Mirror(room);
            sidebar.Observe(room);
            Assert.Equal(Visibility.Visible, panel.Visibility);
            sidebar.Observe(Idle);
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
        });
    }

    [Fact]
    public void QuickChatFiltersBothPickersAndSharesCooldownWithClickablePersistentHistory()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar();
            var view = sidebar.View;
            var room = Room("playing", Self, Self, Peer with { Nickname = "白塔" }) with { RoomId = "7306" };
            sidebar.Server.Snapshot = Mirror(room) with { Shouts = [new("incoming", PublicId(Peer), "我来守左路。")] };
            sidebar.Observe(room);
            var chat = view.ShoutPanel;
            Assert.Single(chat.Feed.Items);
            Assert.False(chat.Send.IsEnabled);
            Click(chat.PickTemplate);
            chat.Types.SelectedItem = "提醒";
            chat.Query.Text = "当心";
            Assert.Contains("当心□！", chat.Results.Items.Cast<string>());
            chat.CommitChoice("当心□！");
            Assert.True(chat.Types.Items.Contains("敌人")); // Selecting a pattern moves straight to the word picker.
            chat.Types.SelectedItem = "敌人";
            chat.Query.Text = "没有这个敌人名";
            Assert.Empty(chat.Results.Items);
            Assert.Equal(Visibility.Visible, chat.NoResults.Visibility);
            chat.Query.Text = "假铳"; // Two nonadjacent characters.
            Assert.Contains("假想敌：铳", chat.Results.Items.Cast<string>());
            chat.Query.Text = "假想";
            var choosing = CaptureQuickChat(view, "quick-chat-picker", 350, 700);
            chat.CommitChoice("假想敌：铳");
            Assert.True(chat.Send.IsEnabled);
            Assert.Equal(Visibility.Collapsed, chat.Chooser.Visibility);
            Click(chat.Send);
            Assert.Equal(1, sidebar.Server.ShoutRequests);
            Assert.Equal(new RecentShout("当心□！", "假想敌：铳"), Assert.Single(sidebar.Settings.LoadRecentShouts()));
            Assert.Equal("当心假想敌：铳！", Assert.IsType<QuickChatPanel.HistoryEntry>(Assert.Single(chat.History.Items)).Text);
            Assert.False(chat.Send.IsEnabled);
            Assert.False(chat.History.IsEnabled);
            var sent = CaptureQuickChat(view, "quick-chat-sent", 350, 595);
            if (choosing != null && sent != null) SaveComparison(choosing, sent);
            var repeat = Assert.Single(Descendants<Button>(chat.History));
            Click(repeat);
            Assert.Equal(1, sidebar.Server.ShoutRequests);
            Assert.True(SpinWait.SpinUntil(() => sidebar.Session.ShoutCooldownMs == 0, TimeSpan.FromSeconds(3)));
            sidebar.Sync();
            Click(repeat);
            Assert.Equal(2, sidebar.Server.ShoutRequests);
            Assert.Single(chat.History.Items);
            var next = room with { RoomId = "new" };
            sidebar.Server.Snapshot = Mirror(next);
            sidebar.Observe(next);
            Assert.Empty(chat.Feed.Items);
            Assert.Single(chat.History.Items);
            Assert.False(chat.Send.IsEnabled);
        });
    }

    [Fact]
    public void RoomNicknamesAreSearchableAndDepartedPlayersCannotBeCalledAgain()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            using var sidebar = new Sidebar();
            var view = sidebar.View;
            var room = Room("playing", Self, Self, Peer, Guest with { Nickname = "旅行博士" });
            var snapshot = Mirror(room) with { RoomNicknames = [Self.Nickname, Peer.Nickname, "旅行博士"] };
            sidebar.Server.Snapshot = snapshot;
            sidebar.Observe(room);
            var chat = view.ShoutPanel;
            Click(chat.PickWord);
            Assert.Contains("房间昵称", chat.Types.Items.Cast<string>());
            chat.Query.Text = "旅博";
            Assert.Contains("旅行博士", chat.Results.Items.Cast<string>());
            chat.Types.SelectedItem = "房间昵称";
            Assert.Equal(new[] { "旅行博士" }, chat.Results.Items.Cast<string>());
            chat.Query.Clear();
            Assert.Equal(new[] { Self.Nickname, Peer.Nickname, "旅行博士" }, chat.Results.Items.Cast<string>());
            CaptureQuickChat(view, "quick-chat-room-nicknames", 350, 700);
            chat.CommitChoice("旅行博士");
            Click(chat.Send);
            Assert.Equal(1, sidebar.Server.ShoutRequests);
            Assert.Equal(new RecentShout("当心□！", "旅行博士", true), Assert.Single(sidebar.Settings.LoadRecentShouts()));
            var changed = Room("playing", Self, Self, Peer);
            Click(chat.PickWord);
            chat.Types.SelectedItem = "房间昵称";
            sidebar.Server.Snapshot = Mirror(changed) with { RoomNicknames = [Self.Nickname, Peer.Nickname] };
            sidebar.Observe(changed);
            Assert.DoesNotContain("旅行博士", chat.Results.Items.Cast<string>());
            Assert.Equal("选择词汇", chat.WordLabel.Text);
            Assert.True(SpinWait.SpinUntil(() => sidebar.Session.ShoutCooldownMs == 0, TimeSpan.FromSeconds(3)));
            sidebar.Sync();
            LineupPerformanceTests.Layout(view);
            var repeat = Assert.Single(Descendants<Button>(chat.History));
            Assert.False(repeat.IsEnabled);
            Click(repeat);
            Assert.Equal(1, sidebar.Server.ShoutRequests);
            sidebar.Server.Snapshot = snapshot;
            sidebar.Observe(room);
            LineupPerformanceTests.Layout(view);
            repeat = Assert.Single(Descendants<Button>(chat.History));
            Assert.True(repeat.IsEnabled);
            Click(repeat);
            Assert.Equal(2, sidebar.Server.ShoutRequests);
        });
    }

    private static BitmapSource? CaptureQuickChat(MatchView view, string name, int width, int height)
    {
        view.Width = width;
        view.Height = height;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        if (Environment.GetEnvironmentVariable("ASPA_QUICK_CHAT_PREVIEWS") is not { Length: > 0 } directory) return null;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        image.Render(view);
        Save(image, Path.Combine(directory, name + ".png"));
        var panel = view.ShoutPanel;
        var origin = panel.TranslatePoint(new Point(), view);
        return new CroppedBitmap(image, new Int32Rect((int)Math.Round(origin.X * 2), (int)Math.Round(origin.Y * 2),
            (int)Math.Round(panel.ActualWidth * 2), (int)Math.Round(panel.ActualHeight * 2)));
    }

    // Contact sheet of the actual WPF controls, not a separate mock-up.
    private static void SaveComparison(BitmapSource choosing, BitmapSource sent)
    {
        var width = 48 + choosing.Width + sent.Width;
        var height = 114 + Math.Max(choosing.Height, sent.Height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(26, 26, 46)), null, new Rect(0, 0, width, height));
            void Label(string text, int size, Brush brush, double x, double y)
            {
                var line = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei UI"), size, brush, 2);
                dc.DrawText(line, new Point(x, y));
            }
            Label("组队  /  游戏进行中", 11, Brushes.LightSkyBlue, 16, 11);
            Label("快捷喊话", 22, Brushes.WhiteSmoke, 16, 30);
            double right = choosing.Width + 32;
            Label("01    按类型和关键词填词", 12, Brushes.LightSteelBlue, 16, 72);
            Label("02    发送、冷却与最近记录", 12, Brushes.LightSteelBlue, right, 72);
            dc.DrawImage(choosing, new Rect(16, 95, choosing.Width, choosing.Height));
            dc.DrawImage(sent, new Rect(right, 95, sent.Width, sent.Height));
        }
        var image = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2),
            192, 192, PixelFormats.Pbgra32);
        image.Render(visual);
        Save(image, Path.Combine(Environment.GetEnvironmentVariable("ASPA_QUICK_CHAT_PREVIEWS")!, "quick-chat.png"));
    }

    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
