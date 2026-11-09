using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class EquipmentBrowseViewModelTests
{
    [Fact]
    public void MultipleTiersAndTypes_UseUnionWithinEachFilterAndIntersectionAcrossFilters()
    {
        var vm = new EquipmentBrowseViewModel();
        vm.LoadEquipment([
            new() { Name = "普通一", Tier = 1 },
            new() { Name = "普通三", Tier = 3 },
            new() { Name = "转职二", Tier = 2 },
            new() { Name = "专属二", Tier = 2, Normal = new() { EffectDescription = "炎层数+1" } }
        ], ["炎"], ["转职二"]);

        vm.SetTierFilters([1, 2]);
        vm.FilteredEquipment.Select(e => e.Name).Should().Equal("普通一", "转职二", "专属二");
        vm.SetTypeFilters(["普通", "转职"]);
        vm.FilteredEquipment.Select(e => e.Name).Should().Equal("普通一", "转职二");
        vm.SearchText = "二";
        vm.FilteredEquipment.Select(e => e.Name).Should().Equal("转职二");
        vm.SearchText = "";
        vm.SetTypeFilters(["普通"]);
        vm.FilteredEquipment.Select(e => e.Name).Should().Equal("普通一");
        vm.SetTierFilters([]);
        vm.FilteredEquipment.Select(e => e.Name).Should().Equal("普通一", "普通三");
        vm.SetTypeFilters([]);
        vm.FilteredEquipment.Should().HaveCount(4);
    }
}
