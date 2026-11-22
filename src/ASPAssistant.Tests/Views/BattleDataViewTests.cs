using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Services;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class BattleDataViewTests
{
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

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var value in Texts(VisualTreeHelper.GetChild(root, i))) yield return value;
    }
}
