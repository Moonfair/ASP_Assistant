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
            view.Present(new(BattleFeedState.WindowsOnly));
            LineupPerformanceTests.Layout(view);
            Assert.DoesNotContain("“暴躁铁皮”", Texts(view));
            view.Present(new(BattleFeedState.PermissionRequired));
            Assert.Equal(Visibility.Visible, ((Button)view.FindName("ConnectButton")).Visibility);
        });
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var value in Texts(VisualTreeHelper.GetChild(root, i))) yield return value;
    }
}
