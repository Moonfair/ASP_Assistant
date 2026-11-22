using System.Windows.Controls;
using System.Windows;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using ASPAssistant.Core.GameModes.GarrisonProtocol;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class GarrisonDebugViewTests
{
    [Fact]
    public void LongDiagnosticsStayFoldedUntilOpenedAndKeepTheReadersChoiceAcrossUpdates()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new GarrisonDebugView { Width = 280, Height = 340 };
            LineupPerformanceTests.Layout(view);
            Expander? Section(string name)
            {
                DependencyObject? node = (DependencyObject)view.FindName(name);
                while (node != null && node is not Expander)
                    node = LogicalTreeHelper.GetParent(node);
                return node as Expander;
            }
            Assert.Null(Section("RoundValue"));
            foreach (var name in new[] { "Teammates", "Characters", "Equipment", "Magic", "Shop", "Positions", "Clock" })
                Assert.False(Assert.IsType<Expander>(Section(name)).IsExpanded);

            var inventory = Section("Characters")!;
            inventory.IsExpanded = true;
            view.Present(new(BattleFeedState.WaitingForRound));
            Assert.True(inventory.IsExpanded);
            Assert.False(Section("Teammates")!.IsExpanded);
            Assert.Equal("尚未读取", ((TextBlock)view.FindName("Characters")).Text);
        });
    }

    [Fact]
    public void TeammatePanelShowsRawGoldAndKeepsCachedGearSeparateFromDeployment()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var view = new GarrisonDebugView { Width = 260, Height = 390 };
            var data = new GarrisonSnapshot(4, 2, 8, 5, 30, null, [], [], [], [],
                Teammates: [new GarrisonTeammate(1, 7, 3, 0, 4, Roster: new(null, null, [new(3,-1,0,0)], null),
                    Overview: new([new(3,"char_demo",[5],1)], [new(5,"item_demo")], null, []))]);
            view.Present(new(BattleFeedState.Live, Garrison: data));
            LineupPerformanceTests.Layout(view);
            var team = (TextBlock)view.FindName("Teammates");
            Assert.Contains("队友 #1", team.Text);
            Assert.Contains("金币 未知（原始值 0）", team.Text);
            Assert.Contains("站位　#3 备战区", team.Text);
            Assert.Contains("队伍概览（缓存）", team.Text);
            Assert.Contains("item_demo · 穿戴于 #3", team.Text);
            Assert.Equal(0, ((ScrollViewer)view.FindName("DebugScroll")).ScrollableWidth);
            view.Present(new(BattleFeedState.Live, Garrison: data with { Teammates = [] }));
            Assert.Equal("当前未发现队友", team.Text);
            view.Present(new(BattleFeedState.WaitingForClient));
            Assert.Equal("尚未读取", team.Text);
        });
    }

    [Fact]
    public void DisplaysOwnedInstancesTheirPositionsAndEquipmentWithoutMixingOcrAndMemory()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var vm = new GameStateViewModel { Hud = new GarrisonHud(11, 4, 24) };
            var view = new GarrisonDebugView { DataContext = vm, Width = 260, Height = 390 };
            view.SetNames(new Dictionary<string, Dictionary<string, string>> {
                ["act3autochess"] = new() { ["chess_a"] = "示例干员", ["item_a"] = "示例装备" }
            }, new Dictionary<string, string>());
            var sample = new GarrisonSnapshot(12, 2, 7, 5, 0, null,
                [new(3, "chess_a", [9], 2), new(4, "chess_a", [], 2)],
                [new(9, "item_a"), new(10, "item_a")], [],
                [new(3, 65539, 0, 2), new(4, -2, 0, 0)], "act3autochess");
            view.Present(new(BattleFeedState.WaitingForRound, Garrison: sample));
            LineupPerformanceTests.Layout(view);
            string Field(string name) => ((TextBlock)view.FindName(name)).Text;
            Assert.Equal("12", Field("RoundValue"));
            Assert.Equal("0", Field("CoinValue"));
            Assert.Contains("场上", Field("Characters"));
            Assert.Contains("备战区", Field("Characters"));
            Assert.Contains("穿戴于 #3 示例干员", Field("Equipment"));
            Assert.Contains("未穿戴", Field("Equipment"));
            Assert.Equal(0, ((ScrollViewer)view.FindName("DebugScroll")).ScrollableWidth);
            view.Present(new(BattleFeedState.PermissionRequired));
            Assert.Equal("尚未读取", Field("Equipment"));
            Assert.Equal("—", Field("RoundValue"));
            Assert.Equal(11, vm.Hud.Round);
            Assert.Equal(Visibility.Visible, ((Button)view.FindName("ConnectButton")).Visibility);
        });
    }
}
