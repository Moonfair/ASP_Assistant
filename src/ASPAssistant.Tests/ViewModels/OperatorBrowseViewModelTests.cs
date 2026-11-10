using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class OperatorBrowseViewModelTests
{
    private static List<Operator> TestOperators =>
    [
        new() { Name = "隐现", Tier = 6, CoreCovenants = ["拉特兰"], AdditionalCovenants = ["迅捷"],
                Normal = new() { Traits = [new() { TraitType = "作战能力", TraitDescription = "攻速+1" }] },
                Elite = new() { Traits = [new() { TraitType = "作战能力", TraitDescription = "攻速+2" }] } },
        new() { Name = "角峰", Tier = 4, CoreCovenants = ["谢拉格"], AdditionalCovenants = ["坚守"],
                Normal = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "层数+2" }] },
                Elite = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "层数+4" }] } },
        new() { Name = "惊蛰", Tier = 6, CoreCovenants = ["炎"], AdditionalCovenants = [],
                Normal = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "炎层数" }] },
                Elite = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "两倍炎层数" }] } }
    ];

    [Fact]
    public void FilterByTier_ReturnsMatchingOnly()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);
        vm.SetTierFilters([4]);
        vm.FilteredOperators.Should().HaveCount(1);
        vm.FilteredOperators[0].Name.Should().Be("角峰");
    }

    [Fact]
    public void FilterByCovenant_ReturnsMatchingOnly()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);
        vm.SetCoreCovenantFilters(["炎"]);
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
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰", "隐现", "惊蛰");
    }

    [Fact]
    public void StackingFilterUsesDescriptionsFromEitherVariantAndCombinesWithTier()
    {
        var operators = TestOperators;
        operators[1].Elite.Traits[0].TraitDescription = "防御力+4";
        operators[2].Normal.Traits[0].TraitDescription = "攻击力+2";
        operators[0].Normal.Traits[0].TraitType = "单次叠加";
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(operators);

        vm.SelectedStackingFilter = true;
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰", "惊蛰");
        vm.ActiveFilterTags.Select(t => t.Label).Should().Contain("叠层");

        vm.SetTierFilters([6]);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("惊蛰");

        vm.SelectedStackingFilter = false;
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("隐现");
        vm.ActiveFilterTags.Select(t => t.Label).Should().Contain("非叠层");

        vm.SelectedStackingFilter = null;
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("隐现", "惊蛰");
    }

    [Fact]
    public void MultipleTiers_CombineWithSearchAndBans_AndStaySortedAfterClearing()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);
        vm.SetTierFilters([6, 4, 6]);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰", "隐现", "惊蛰");
        vm.ActiveFilterTags.Select(t => t.Label).Should().Equal("Ⅳ", "Ⅵ");
        vm.SearchText = "隐";
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("隐现");
        vm.SearchText = "";
        vm.SetBanChecker(name => name == "隐现");
        vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰", "惊蛰");
        vm.SelectedAvailabilityFilter = AvailabilityFilter.Unavailable;
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("隐现");
        vm.SelectedAvailabilityFilter = AvailabilityFilter.All;
        vm.SetTierFilters([]);
        vm.NotifyBansChanged();
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰", "隐现", "惊蛰");
        vm.ActiveFilterTags.Select(t => t.Label).Should().BeEmpty();
    }

    [Fact]
    public void MultipleTraits_MatchEitherVariant_AndIntersectWithTierAndStacking()
    {
        var operators = TestOperators;
        operators[2].Normal.Traits[0].TraitType = "经济";
        operators[2].Elite.Traits[0].TraitType = "精英专属";
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(operators);
        vm.SetTraitTypeFilters(["作战能力", "精英专属"]);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("隐现", "惊蛰");
        vm.ActiveFilterTags.Select(t => t.Label).Should().Contain("作战能力").And.Contain("精英专属");
        vm.SelectedStackingFilter = true;
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("惊蛰");
        vm.SetTierFilters([4]);
        vm.FilteredOperators.Should().BeEmpty();
        vm.SetTraitTypeFilters([]);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰");
        vm.SelectedStackingFilter = false;
        vm.FilteredOperators.Should().BeEmpty();
    }

    [Fact]
    public void MultipleTimings_MatchExactTagsInEitherVariant_AndIntersectWithCovenants()
    {
        var operators = TestOperators;
        operators[0].Normal.Traits[0].TraitDescription = "<获得时>攻击力+1";
        operators[1].Elite.Traits[0].TraitDescription = "<休整期结束时>层数+4";
        operators[2].Normal.Traits[0].TraitDescription = "<未获得时>层数+2";
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(operators);
        vm.SetTriggerTimingFilters(["获得时", "休整期结束时"]);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰", "隐现");
        vm.ActiveFilterTags.Select(t => t.Label).Should().Contain("<获得时>").And.Contain("<休整期结束时>");
        vm.SetCoreCovenantFilters(["拉特兰", "谢拉格"]);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰", "隐现");
        vm.SetOtherCovenantFilters(["迅捷", "坚守"]);
        vm.FilteredOperators.Should().HaveCount(2);
        vm.SetOtherCovenantFilters(["坚守"]);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰");
        vm.SetCoreCovenantFilters([]);
        vm.SetOtherCovenantFilters([]);
        vm.SetTriggerTimingFilters([]);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("角峰", "隐现", "惊蛰");
        vm.ActiveFilterTags.Select(t => t.Label).Should().BeEmpty();
    }
}
