using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class CovenantBanRuleTests
{
    [Theory]
    [InlineData("萨尔贡", "精准", true)]
    [InlineData("萨尔贡", "坚守", true)]
    [InlineData("炎", "精准", false)]
    [InlineData("炎", "坚守", false)]
    [InlineData("萨尔贡", "", true)]
    [InlineData("炎", "", false)]
    [InlineData("", "精准", true)]
    [InlineData("", "精准,远见", true)]
    [InlineData("", "精准,坚守", false)]
    [InlineData("", "", true)]
    [InlineData("萨尔贡,炎", "", true)]
    [InlineData("炎,叙拉古", "精准", false)]
    [InlineData("萨尔贡,炎", "精准,坚守", true)]
    [InlineData("萨尔贡,炎", "坚守", true)]
    public void AppliesTheInGameAvailabilityRules(string core, string additional, bool banned)
    {
        var candidate = new BanCandidate("测试", core.Split(',', StringSplitOptions.RemoveEmptyEntries),
            additional.Split(',', StringSplitOptions.RemoveEmptyEntries), []);
        PreparationScanner.ShouldBan(candidate, ["炎", "叙拉古"], ["坚守"]).Should().Be(banned);
    }
}
