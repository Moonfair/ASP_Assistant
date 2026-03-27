using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class OperatorBrowseViewModelTests
{
    private static List<Operator> TestOperators =>
    [
        new() { Name = "隐现", Tier = 6, CoreCovenant = "拉特兰", AdditionalCovenants = ["迅捷"],
                Normal = new() { Traits = [new() { TraitType = "作战能力", TraitDescription = "攻速+1" }] },
                Elite = new() { Traits = [new() { TraitType = "作战能力", TraitDescription = "攻速+2" }] } },
        new() { Name = "角峰", Tier = 4, CoreCovenant = "谢拉格", AdditionalCovenants = ["坚守"],
                Normal = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "层数+2" }] },
                Elite = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "层数+4" }] } },
        new() { Name = "惊蛰", Tier = 6, CoreCovenant = "炎", AdditionalCovenants = [],
                Normal = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "炎层数" }] },
                Elite = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "两倍炎层数" }] } }
    ];

    [Fact]
    public void FilterByTier_ReturnsMatchingOnly()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);
        vm.SelectedTierFilter = 4;
        vm.FilteredOperators.Should().HaveCount(1);
        vm.FilteredOperators[0].Name.Should().Be("角峰");
    }

    [Fact]
    public void FilterByCovenant_ReturnsMatchingOnly()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);
        vm.SelectedCovenantFilter = "炎";
        vm.FilteredOperators.Should().HaveCount(1);
        vm.FilteredOperators[0].Name.Should().Be("惊蛰");
    }

    [Fact]
    public void SearchByName_ReturnsMatchingOnly()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);
        vm.SearchText = "角";
        vm.FilteredOperators.Should().HaveCount(1);
        vm.FilteredOperators[0].Name.Should().Be("角峰");
    }

    [Fact]
    public void NoFilter_ReturnsAll()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);
        vm.FilteredOperators.Should().HaveCount(3);
    }
}
