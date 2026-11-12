using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class TrackingViewModelTests
{
    [Fact]
    public void TrackCovenantCombination_AddsOnlyOperatorsWithBothCovenants_AndNotifiesOnce()
    {
        var vm = new TrackingViewModel([
            new() { Name = "已追踪", CoreCovenants = ["萨尔贡"], AdditionalCovenants = ["迅捷"] },
            new() { Name = "多核心", CoreCovenants = ["莱塔尼亚", "萨尔贡"], AdditionalCovenants = ["迅捷"], IconPath = "multi-core.png" },
            new() { Name = "多附加", CoreCovenants = ["萨尔贡"], AdditionalCovenants = ["坚守", "迅捷"] },
            new() { Name = "仅核心匹配", CoreCovenants = ["萨尔贡"], AdditionalCovenants = ["坚守"] },
            new() { Name = "仅附加匹配", CoreCovenants = ["莱塔尼亚"], AdditionalCovenants = ["迅捷"] },
            new() { Name = "无附加盟约", CoreCovenants = ["萨尔贡"] }
        ]);
        vm.AddTracking("已追踪", TrackingType.Operator);
        vm.AddTracking("长剑", TrackingType.Equipment);
        var snapshots = new List<int>();
        vm.TrackingChanged += () => snapshots.Add(vm.AllEntries.Count);

        vm.TrackCovenantCombination("萨尔贡", "迅捷").Should().Be((3, 2));
        vm.TrackCovenantCombination("萨尔贡", "迅捷").Should().Be((3, 0));
        vm.TrackCovenantCombination("莱塔尼亚", "坚守").Should().Be((0, 0));
        vm.TrackedOperators.Select(item => item.Name).Should().Equal("已追踪", "多核心", "多附加");
        vm.TrackedOperators[1].IconPath.Should().Be("multi-core.png");
        vm.TrackedEquipment.Select(item => item.Name).Should().Equal("长剑");
        snapshots.Should().Equal(4);
    }

    [Fact]
    public void TrackLineup_AddsBothSlotsAndOperatorsOnce_AndNotifiesAfterTheBatch()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        var snapshots = new List<int>();
        vm.TrackingChanged += () => snapshots.Add(vm.AllEntries.Count);
        var lineup = new Lineup
        {
            Members =
            [
                new() { OperatorName = "角峰", Equipment1 = "长剑", Equipment2 = "法杖" },
                new() { OperatorName = "地灵", Equipment2 = "长剑" }
            ]
        };
        vm.TrackLineup(lineup).Should().Be(3);
        vm.TrackLineup(lineup).Should().Be(0);
        vm.TrackedOperators.Select(item => item.Name).Should().Equal("角峰", "地灵");
        vm.TrackedEquipment.Select(item => item.Name).Should().Equal("长剑", "法杖");
        snapshots.Should().Equal(4);
    }

    [Fact]
    public void AddTracking_AddsEntry()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.TrackedOperators.Should().ContainSingle(e => e.Name == "角峰");
    }

    [Fact]
    public void AddTracking_Duplicate_DoesNotAdd()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.TrackedOperators.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveTracking_RemovesEntry()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.RemoveTracking("角峰");
        vm.TrackedOperators.Should().BeEmpty();
    }

    [Fact]
    public void ClearTracking_ClearsBothListsBeforeNotifyingOnce()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.AddTracking("不屈弹射器", TrackingType.Equipment);
        var snapshots = new List<int>();
        vm.TrackingChanged += () => snapshots.Add(vm.AllEntries.Count);

        vm.ClearTracking();
        vm.ClearTracking();

        vm.TrackedOperators.Should().BeEmpty();
        vm.TrackedEquipment.Should().BeEmpty();
        snapshots.Should().Equal(0);
    }

    [Fact]
    public void TrackingEntries_ResolveCatalogIconsForSavedAndNewItems()
    {
        var vm = new TrackingViewModel(
            [new Operator { Name = "角峰", IconPath = "icons/operators/matterhorn.png" }],
            [new Equipment { Name = "不屈弹射器", IconPath = "icons/equipment/不屈弹射器.png" }]);
        vm.LoadEntries([
            new TrackingEntry { Name = "角峰", Type = TrackingType.Operator },
            new TrackingEntry { Name = "旧装备", Type = TrackingType.Equipment }
        ]);
        vm.AddTracking("不屈弹射器", TrackingType.Equipment);

        vm.TrackedOperators.Single().IconPath.Should().Be("icons/operators/matterhorn.png");
        vm.TrackedEquipment.Select(entry => entry.IconPath).Should().Equal("", "icons/equipment/不屈弹射器.png");
    }

    [Fact]
    public void IsTracked_ReturnsTrueForTrackedItem()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.IsTracked("角峰").Should().BeTrue();
        vm.IsTracked("隐现").Should().BeFalse();
    }

    [Fact]
    public void AllEntries_ReturnsBothTypes()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.AddTracking("不屈弹射器", TrackingType.Equipment);
        vm.AllEntries.Should().HaveCount(2);
        vm.TrackedOperators.Should().HaveCount(1);
        vm.TrackedEquipment.Should().HaveCount(1);
    }
}
