using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class BattleDataViewTests
{
    [Theory]
    [InlineData(GameTargetKind.Windows)]
    [InlineData(GameTargetKind.MuMu)]
    public void RestrictedSourceAndReconnectNeverStartAReader(GameTargetKind kind)
    {
        LineupPerformanceTests.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var tokens = new System.Collections.Concurrent.ConcurrentQueue<CancellationToken>();
            Task Connect(CancellationToken token) { tokens.Enqueue(token); return Task.Delay(-1, token); }
            var view = new BattleDataView((_, _, _, token) => Connect(token), (_, _, token) => Connect(token));
            try
            {
                var target = new GameTarget("test", "test", kind, 1, 1);
                var states = new List<BattleUpdate>();
                view.Updated += states.Add;
                view.SetSource(target, kind == GameTargetKind.Windows);
                view.Reconnect();
                Assert.DoesNotContain(states, s => s.State == BattleFeedState.Connecting);
                Assert.Empty(tokens);
                view.SetMemoryReadingAllowed(true);
                IconLoadingTests.PumpUntil(() => tokens.Count == 1);
                view.Present(new(BattleFeedState.Live, new(1, 100, 5, false,
                    new() { ["operator"] = new(100, 0, 0, 0) })));
                view.SetMemoryReadingAllowed(false);
                Assert.True(tokens.First().IsCancellationRequested);
                Assert.Null(((ItemsControl)view.FindName("OperatorRows")).ItemsSource);
                Assert.Equal(BattleFeedState.Restricted, states.Last().State);
                states.Clear();
                view.SetSource(target with { Id = "next" }, kind == GameTargetKind.Windows);
                view.Reconnect();
                Assert.DoesNotContain(states, s => s.State == BattleFeedState.Connecting);
                view.SetMemoryReadingAllowed(true);
                IconLoadingTests.PumpUntil(() => tokens.Count == 2);
            }
            finally { view.Stop(); }
        });
    }

    [Fact]
    public void StartsWhileHiddenAndReusesTheAuthorizedConnectionWhenTheGameAppears()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var started = new TaskCompletionSource<Func<BattleFeedMessage, Task>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int connects = 0;
            var view = new BattleDataView((pid, mayPrompt, publish, token) =>
            {
                Interlocked.Increment(ref connects);
                started.TrySetResult(publish);
                return Task.Delay(Timeout.Infinite, token);
            });
            try
            {
                Assert.False(view.IsVisible);
                view.SetMemoryReadingAllowed(true);
                view.SetSource(null, false);
                Assert.Equal(0, connects);
                view.SetSource(null, true);
                IconLoadingTests.PumpUntil(() => started.Task.IsCompleted);
                var target = new GameTarget(GameTarget.WindowsId, "Windows", GameTargetKind.Windows, 1, 1, WindowPid: 44);
                view.SetSource(target, true);
                view.SetSource(target, true);
                var seen = new List<BattleUpdate>();
                view.Updated += seen.Add;
                var publish = started.Task.GetAwaiter().GetResult();
                var wrong = publish(new(43, new(BattleFeedState.Live)));
                IconLoadingTests.PumpUntil(() => wrong.IsCompleted);
                Assert.Empty(seen);
                var right = publish(new(44, new(BattleFeedState.WaitingForRound)));
                IconLoadingTests.PumpUntil(() => right.IsCompleted);
                Assert.Equal(BattleFeedState.WaitingForRound, Assert.Single(seen).State);
                Assert.Equal(1, connects);
                view.SetSource(null, false);
                Assert.Equal(BattleFeedState.WindowsOnly, seen.Last().State);
            }
            finally { view.Stop(); }
        });
    }

    [Fact]
    public void MinimumWidthShowsDamageSharesFromTheFirstSnapshotAndClearsThemOnSourceChange()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new BattleDataView { Width = 260, Height = 340 };
            view.SetNames(new Dictionary<string, string> { ["char_618_acrbot"] = "“暴躁铁皮”" });
            var damage = new[] { 2739473, 935605, 346118, 297196, 31588, 15988, 1053, 0 };
            var counters = damage.Select((amount, index) => (amount, index)).ToDictionary(
                x => x.index == 0 ? "char_618_acrbot" : $"operator_{x.index}",
                x => new OperatorCounters(x.amount, 0, 0, 0));
            view.Present(new(BattleFeedState.Live, new(1, 2154, 71.8, false, counters)));
            LineupPerformanceTests.Layout(view);
            var texts = Texts(view).ToArray();
            Assert.Contains("“暴躁铁皮”", texts);
            Assert.Contains("伤害占比", texts);
            Assert.Contains("4,367,021", texts);
            Assert.Contains("2,739,473", texts);
            Assert.Contains("62.73%", texts);
            Assert.Contains("21.42%", texts);
            Assert.Contains("0.00%", texts);
            var scroll = (ScrollViewer)view.FindName("RowsScroll");
            Assert.Equal(0, scroll.ScrollableWidth);
            var emulator = new GameTarget("ld:0", "LD", GameTargetKind.LdPlayer, 1, 1, WindowPid: 77);
            view.SetMemoryReadingAllowed(true);
            view.SetSource(emulator, true);
            LineupPerformanceTests.Layout(view);
            Assert.DoesNotContain("“暴躁铁皮”", Texts(view));
            Assert.Contains("当前来源无法读取干员伤害。战斗统计支持官方 PC 客户端和 MuMu。", Texts(view));
            Assert.Equal(Visibility.Collapsed, ((Button)view.FindName("ConnectButton")).Visibility);
            view.Present(new(BattleFeedState.PermissionRequired));
            Assert.Equal(Visibility.Visible, ((Button)view.FindName("ConnectButton")).Visibility);
        });
    }

    [Fact]
    public void SameNameRowsKeepTheirOwnPortraitNumberAndShareWhenRanksChange()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new BattleDataView { Width = 260, Height = 340 };
            var names = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"))
                .LoadBattleOperatorNamesAsync().GetAwaiter().GetResult();
            view.SetNames(names);
            const string first = "char_4025_aprot2", second = "char_512_aprot";
            void Frame(int round, uint frame, string id, int a, int b)
            {
                view.Present(new(BattleFeedState.Live, new(round, frame, frame, false, new()
                {
                    [id] = new(a, 0, 0, 0), [second] = new(b, 0, 0, 0), ["char_unknown"] = new(0, 0, 0, 0)
                })));
                LineupPerformanceTests.Layout(view);
            }
            var list = (ItemsControl)view.FindName("OperatorRows");
            DependencyObject Row(int rank) => list.ItemContainerGenerator.ContainerFromIndex(rank);
            void CheckRow(int rank, string number, string share)
            {
                var row = Row(rank);
                Assert.Contains("暮落", Texts(row));
                Assert.Contains(number, Texts(row));
                Assert.Contains(share, Texts(row));
                var portrait = Assert.Single(Visuals<Image>(row));
                Assert.Equal("icons/operators/暮落.png", DeferredIcon.GetPath(portrait));
                IconLoadingTests.PumpUntil(() => portrait.Source != null);
            }
            Frame(1, 1, first, 30, 70);
            CheckRow(0, "#2", "70.00%");
            CheckRow(1, "#1", "30.00%");
            Assert.Contains("char_unknown", Texts(Row(2)));
            Assert.Null(Assert.Single(Visuals<Image>(Row(2))).Source);
            Frame(1, 2, first, 210, 70);
            CheckRow(0, "#1", "75.00%");
            CheckRow(1, "#2", "25.00%");
            // Independently keyed instances of the same character also share the catalog portrait.
            Frame(2, 1, second + "#7", 60, 40);
            CheckRow(0, "#2", "60.00%");
            CheckRow(1, "#1", "40.00%");
            Assert.Equal(0, ((ScrollViewer)view.FindName("RowsScroll")).ScrollableWidth);
        });
    }

    [Fact]
    public async Task KnownBattleOperatorsHavePackagedPortraits()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var names = await new JsonDataStore(data).LoadBattleOperatorNamesAsync();
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names.Values.Distinct(), name => !File.Exists(Path.Combine(data, "icons", "operators", name + ".png")));
    }

    [Fact]
    public void MuMuPublishesToTheSameRowsAndReleasesTheOldInstanceOnSwitch()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var feeds = new System.Collections.Concurrent.ConcurrentDictionary<string,
                (Func<BattleUpdate, Task> Publish, CancellationToken Token)>();
            int windows = 0;
            var view = new BattleDataView((_, _, _, token) => { Interlocked.Increment(ref windows); return Task.Delay(-1, token); },
                (t, publish, token) => { feeds[t.Id] = (publish, token); return Task.Delay(-1, token); });
            try
            {
                var one = new GameTarget("mumu:0", "MuMu", GameTargetKind.MuMu, 1, 1, Serial: "127.0.0.1:16384");
                view.SetMemoryReadingAllowed(true);
                view.SetSource(one, false);
                IconLoadingTests.PumpUntil(() => feeds.ContainsKey(one.Id));
                var first = feeds[one.Id];
                var unsupported = first.Publish(new(BattleFeedState.UnsupportedBuild));
                IconLoadingTests.PumpUntil(() => unsupported.IsCompletedSuccessfully);
                Assert.Equal(Visibility.Visible, ((Button)view.FindName("ConnectButton")).Visibility);
                var update = new BattleUpdate(BattleFeedState.Live, new(1, 30, 1, false,
                    new() { ["char_probe"] = new(40, 20, 30, 10) }));
                var task = first.Publish(update);
                IconLoadingTests.PumpUntil(() => task.IsCompletedSuccessfully);
                LineupPerformanceTests.Layout(view);
                Assert.Contains("char_probe", Texts(view));
                Assert.Contains("100.00%", Texts(view));
                var two = one with { Id = "mumu:1", Serial = "127.0.0.1:16416" };
                view.SetSource(two, false);
                IconLoadingTests.PumpUntil(() => feeds.ContainsKey(two.Id));
                Assert.True(first.Token.IsCancellationRequested);
                var stale = first.Publish(update);
                IconLoadingTests.PumpUntil(() => stale.IsCompleted);
                LineupPerformanceTests.Layout(view);
                Assert.DoesNotContain("char_probe", Texts(view));
                Assert.Equal(0, windows);
                view.Stop();
                Assert.True(feeds[two.Id].Token.IsCancellationRequested);
            }
            finally { view.Stop(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepsLastDamageUntilTheNextBattleEvenIfTheEndFrameIsMissed(bool receivedEnd)
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new BattleDataView();
            var frame = new BattleFrame(1, 120, 4, false, new() { ["first"] = new(30, 0, 0, 0) });
            view.Present(new(BattleFeedState.Live, frame));
            if (receivedEnd)
                view.Present(new(BattleFeedState.Live, frame with { Ended = true,
                    Operators = new() { ["first"] = new(42, 0, 0, 0) } }));
            foreach (var state in new[] { BattleFeedState.WaitingForRound, BattleFeedState.ReadInterrupted,
                BattleFeedState.ConnectionFailed, BattleFeedState.Connecting, BattleFeedState.WaitingForRound })
            {
                view.Present(new(state));
                LineupPerformanceTests.Layout(view);
                Assert.Contains("first", Texts(view));
                Assert.Equal(receivedEnd ? "42" : "30", view.TotalDamage.Text);
                Assert.Contains("100.00%", Texts(view));
                Assert.Equal("0:04.0", view.GameClock.Text);
                if (state == BattleFeedState.WaitingForRound)
                    Assert.Equal("上回合数据 · 等待战斗", view.FeedStatus.Text);
            }

            view.Present(new(BattleFeedState.Live, new(2, 0, 0, false, [])));
            Assert.Empty(view.OperatorRows.Items);
            Assert.Equal("0", view.TotalDamage.Text);
            Assert.Equal("0:00.0", view.GameClock.Text);
            view.Present(new(BattleFeedState.Live, new(2, 1, .1, false, new() { ["second"] = new(8, 0, 0, 0) })));
            Assert.Single(view.OperatorRows.Items);
            Assert.Equal("8", view.TotalDamage.Text);
        });
    }

    [Fact]
    public void LivePreparationFramesDoNotOverwriteTheResultsBeforeTheNextCombatPhase()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new BattleDataView();
            GarrisonSnapshot Phase(int state) => new(1, 0, null, null, null, null, null, null, null, null,
                Phase: new(state, 3, null));
            var battle = new BattleFrame(1, 100, 3.3, false, new() { ["first"] = new(40, 0, 0, 0) });
            view.Present(new(BattleFeedState.Live, battle, Phase(4)));
            var empty = new BattleFrame(1, 110, 3.6, false, []);
            view.Present(new(BattleFeedState.Live, empty, Phase(7))); // Settlement
            view.Present(new(BattleFeedState.Live, empty, Phase(3))); // Preparation
            view.Present(new(BattleFeedState.Live, empty, GarrisonInterrupted: true));
            Assert.Single(view.OperatorRows.Items);
            Assert.Equal("40", view.TotalDamage.Text);
            Assert.Equal("0:03.3", view.GameClock.Text);

            // The phase can arrive before the first readable battle frame.
            view.Present(new(BattleFeedState.WaitingForRound, Garrison: Phase(6)));
            Assert.Empty(view.OperatorRows.Items);
            Assert.Equal("", view.GameClock.Text);
            view.Present(new(BattleFeedState.Live, battle with { Operators = new() { ["second"] = new(9, 0, 0, 0) } }, Phase(6)));
            LineupPerformanceTests.Layout(view);
            Assert.Contains("second", Texts(view));
            Assert.DoesNotContain("first", Texts(view));
            Assert.Equal("9", view.TotalDamage.Text);
        });
    }

    [Fact]
    public void EmptyEndFrameDoesNotEraseDamageAndResumingTheSameFrameRestoresLiveStatus()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new BattleDataView();
            var frame = new BattleFrame(1, 30, 1, false, new() { ["first"] = new(15, 0, 0, 0) });
            view.Present(new(BattleFeedState.Live, frame));
            view.Present(new(BattleFeedState.ReadInterrupted));
            view.Present(new(BattleFeedState.Live, frame));
            Assert.Equal("本回合战斗数据", view.FeedStatus.Text);
            view.Present(new(BattleFeedState.Live, frame with { Frame = 31, GameSeconds = 1.1, Operators = [] }));
            Assert.Equal("15", view.TotalDamage.Text);
            Assert.Equal("0:01.0", view.GameClock.Text);
            view.Present(new(BattleFeedState.Live, frame with { Ended = true, Operators = [] }));
            Assert.Equal("本回合已结束", view.FeedStatus.Text);
            Assert.Equal("15", view.TotalDamage.Text);
            Assert.Single(view.OperatorRows.Items);
        });
    }

    [Fact]
    public void ANewFrameOnlyRoundCanFollowAGarrisonBattle()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new BattleDataView();
            var frame = new BattleFrame(1, 30, 1, false, new() { ["first"] = new(15, 0, 0, 0) });
            view.Present(new(BattleFeedState.Live, frame));
            view.Present(new(BattleFeedState.WaitingForRound, Garrison: new(2, 0, null, null, null, null,
                null, null, null, null, Phase: new(3, 3, null))));
            view.Present(new(BattleFeedState.Live, new(2, 0, 0, false, [])));
            Assert.Empty(view.OperatorRows.Items);
            Assert.Equal("0:00.0", view.GameClock.Text);
        });
    }

    private static IEnumerable<string> Texts(DependencyObject root) => Visuals<TextBlock>(root).Select(t => t.Text);

    [Fact]
    public void MuMuBoardCanUpdateBeforeLocalDamageAndClearsOnOrdinaryCombat()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new BattleDataView { Width = 260, Height = 420 };
            view.SetNames(new Dictionary<string, string>(), new Dictionary<string, Dictionary<string, string>> {
                ["act3autochess"] = new() { ["chess_a"] = "干员甲" } });
            void Show(string wire)
            {
                view.Present(MuMuBattleFeed.Decode(wire));
                LineupPerformanceTests.Layout(view);
            }
            Show("idle|6,14,1,act3autochess,1000000;2,chess_a,69000;1,chess_a,20000");
            Assert.Equal(Visibility.Visible, view.BossSummary.Visibility);
            Assert.Contains("干员甲", Texts(view.BossSummary));
            Assert.Contains("69,000", Texts(view.BossSummary));
            Assert.Contains("6%", Texts(view.BossSummary));
            Assert.Contains("自己", Texts(view.BossSummary));
            Assert.Contains("玩家 2", Texts(view.BossSummary));
            Assert.Equal(Visibility.Collapsed, view.Summary.Visibility);
            Show("idle|7,14,1,act3autochess,0");
            Assert.Contains("6%", Texts(view.BossSummary));
            Show("frame\t2\t30\t4294967296\t0\tchar_a:10:0:0:0|4,15,1,act3autochess,0");
            Assert.Equal(Visibility.Collapsed, view.BossSummary.Visibility);
            Assert.Equal("10", view.TotalDamage.Text);
        });
    }

    [Fact]
    public void BossRankingIsSeparateFromGlobalDamageAndRetainedOnlyUntilTheNextBattle()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new BattleDataView { Width = 260, Height = 420 };
            view.SetNames(new Dictionary<string, string>(), new Dictionary<string, Dictionary<string, string>> {
                ["fixture"] = new() { ["chess_1"] = "领袖榜干员" } });
            var frame = new BattleFrame(1, 20, 1, false, new() { ["BossA"] = new(350000, 0, 0, 0) });
            void Show(int state, int? damage = null, int? hp = 1000000)
            {
                var board = damage.HasValue ? new BossDamageBoard(hp, [
                    new(2, "队友", "chess_1", 22000), new(1, "测试玩家", "chess_1", damage.Value)]) : null;
                view.Present(new(BattleFeedState.Live, frame, new(14, 1, null, null, null, null, null, null, null, null,
                    ActivityId: "fixture", Phase: new(state, 3, null), BossDamage: board)));
                LineupPerformanceTests.Layout(view);
            }
            Show(4);
            var panel = Assert.IsAssignableFrom<FrameworkElement>(view.FindName("BossSummary"));
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Show(6);
            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Contains("等待领袖伤害数据", Texts(panel));
            Show(6, 69000);
            Assert.Contains("对领袖伤害", Texts(panel));
            Assert.Contains("69,000", Texts(panel));
            Assert.Contains("6%", Texts(panel)); // Game truncates 6.9%; not share of team/global damage.
            Assert.Contains("2%", Texts(panel));
            Assert.Contains("队友", Texts(panel));
            Assert.Contains("领袖榜干员", Texts(panel));
            Assert.Contains("测试玩家", Texts(view.BossRows.ItemContainerGenerator.ContainerFromIndex(0)));
            Assert.Equal(0, view.RowsScroll.ScrollableWidth);
            Assert.Equal("350,000", view.TotalDamage.Text);
            Assert.True(panel.TranslatePoint(new Point(), view).Y < view.Summary.TranslatePoint(new Point(), view).Y);
            // The server board can change while the local combat frame is identical.
            Show(6, 102000, hp: null);
            Assert.Contains("102,000", Texts(panel));
            Assert.Contains("—", Texts(panel));
            Show(6);
            Show(7);
            view.Present(new(BattleFeedState.ReadInterrupted));
            Assert.Contains("102,000", Texts(panel));
            Show(4);
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Show(6);
            Assert.DoesNotContain("102,000", Texts(panel));
        });
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var value in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return value;
    }
}
