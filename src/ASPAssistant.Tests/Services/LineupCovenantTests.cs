using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class LineupCovenantTests
{
    [Fact]
    public async Task CalculatesActivationFromSavedLineupAndEquipmentOnEachCarrier()
    {
        var data = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = await data.LoadOperatorsAsync();
        var (equipment, _) = await data.LoadEquipmentAsync();
        var calculator = new LineupCovenantCalculator(await data.LoadCovenantCatalogAsync(), operators);
        var lineup = new Lineup
        {
            Name = "盟约验收", Author = "博士", CovenantTags = ["莱塔尼亚"],
            Members =
            [
                new() { OperatorName = "黍", IsElite = true, Equipment1 = "变形同构体", Equipment2 = "炎国短刀" },
                new() { OperatorName = "余", IsElite = true },
                new() { OperatorName = "魔王" },
                new() { OperatorName = "盟约·辅助干员" },
                new() { OperatorName = "艾丝黛尔" },
                new() { OperatorName = "伊桑", Equipment1 = "变形同构体", Equipment2 = "精准狙击镜" },
                new() { OperatorName = "“暴躁铁皮”", Equipment3 = "变形同构体", Equipment4 = "精准狙击镜" }
            ]
        };

        var editor = new LineupEditorViewModel(operators, equipment, lineup);
        var codec = LineupShareService.FromCatalog(operators, equipment.Select(item => item.Name));
        var restored = codec.Import(editor.ExportLineup(editor.BuildLineup()));
        calculator.GetActiveCovenants(restored).Should().Equal("炎", "精准", "调和", "独行", "绝技");

        var members = restored.Members;
        calculator.GetActiveCovenants(restored with
        {
            Members = [members[0], members[1] with { IsElite = false }, members[4], new() { OperatorName = "地灵" },
                members[5], members[6] with { Equipment3 = null }]
        }).Should().BeEmpty();
    }
}
