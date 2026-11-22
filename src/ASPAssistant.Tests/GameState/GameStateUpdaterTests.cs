using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.GameState;

public class GameStateUpdaterTests
{
    [Fact]
    public void NativeHudRemainsLiveThroughScreenMissesAndClearsWhenMemoryDisconnects()
    {
        var vm = new GameStateViewModel();
        vm.SetHudSource(GameTargetKind.Windows, memoryReadingAllowed: true);
        var screen = new Core.GameState.GameState { Hud = new GarrisonHud(1, 2, 99, 7) };
        vm.UpdateFromGameState(screen);
        Assert.Null(vm.Hud.Round);
        var snapshot = new GarrisonSnapshot(12, 0, 8, 3, 98,
            new(5, 0, 7, 2, 0, []), [], [], [], []);
        vm.UpdateMemoryHud(snapshot);
        Assert.Equal(new GarrisonHud(12, 5, 0, 7), vm.Hud);
        Assert.Equal(screen.Hud, vm.ScreenHud);
        vm.UpdateFromGameState(new());
        Assert.Equal(12, vm.Hud.Round);
        Assert.Equal(0, vm.Hud.Gold);
        vm.UpdateMemoryHud(null);
        vm.UpdateFromGameState(screen);
        Assert.Null(vm.Hud.Gold);
        vm.SetHudSource(GameTargetKind.Windows, memoryReadingAllowed: false);
        Assert.Null(vm.Hud.Round);
        vm.UpdateFromGameState(screen);
        vm.UpdateMemoryHud(snapshot);
        Assert.Equal(screen.Hud, vm.Hud);
        vm.SetHudSource(GameTargetKind.MuMu, memoryReadingAllowed: true);
        vm.UpdateMemoryHud(snapshot);
        vm.UpdateFromGameState(screen);
        Assert.Equal(screen.Hud, vm.Hud);
    }

    [Fact]
    public void HudPreservesZeroGoldAndClearsWhenTheMatchIsUnavailable()
    {
        var viewModel = new GameStateViewModel();
        viewModel.UpdateFromGameState(new Core.GameState.GameState { Hud = new GarrisonHud(12, null, 0, 5) });

        Assert.Equal(new GarrisonHud(12, null, 0, 5), viewModel.Hud);
        Assert.Equal(16, viewModel.Hud.NextRoundGold);

        viewModel.UpdateFromGameState(new Core.GameState.GameState());
        Assert.Equal(default, viewModel.Hud);
        Assert.Null(viewModel.Hud.NextRoundGold);
    }

    [Fact]
    public void UpdateShop_MatchesTrackedItems()
    {
        var tracked = new List<TrackingEntry>
        {
            new() { Name = "角峰", Type = TrackingType.Operator },
            new() { Name = "不屈弹射器", Type = TrackingType.Equipment }
        };
        var shopItems = new List<ShopItem>
        {
            new() { Name = "隐现", Price = 8 },
            new() { Name = "角峰", Price = 4 },
            new() { Name = "不屈弹射器", Price = 3 }
        };

        GameStateUpdater.UpdateShopTracking(shopItems, tracked);

        shopItems.First(s => s.Name == "角峰").IsTracked.Should().BeTrue();
        shopItems.First(s => s.Name == "不屈弹射器").IsTracked.Should().BeTrue();
        shopItems.First(s => s.Name == "隐现").IsTracked.Should().BeFalse();
    }

    [Fact]
    public void ComputeCovenantCounts_FromFieldAndBench()
    {
        var operators = new List<Operator>
        {
            new() { Name = "角峰", CoreCovenants = ["谢拉格"], AdditionalCovenants = ["坚守"] },
            new() { Name = "深巡", CoreCovenants = ["阿戈尔"], AdditionalCovenants = [] },
            new() { Name = "红豆", CoreCovenants = [], AdditionalCovenants = ["不屈"] }
        };
        var owned = new List<(string Name, int Count)>
        {
            ("角峰", 2), ("深巡", 1), ("红豆", 1)
        };

        var counts = GameStateUpdater.ComputeCovenantCounts(owned, operators);

        counts["谢拉格"].Should().Be(2);
        counts["阿戈尔"].Should().Be(1);
        counts["不屈"].Should().Be(1);
        counts["坚守"].Should().Be(2);
    }
}
