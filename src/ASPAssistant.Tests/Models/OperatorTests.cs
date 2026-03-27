using System.Text.Json;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Models;

public class OperatorTests
{
    [Fact]
    public void Deserialize_ValidJson_ReturnsOperator()
    {
        var json = """
        {
            "name": "隐现",
            "tier": 6,
            "coreCovenant": "拉特兰",
            "additionalCovenants": ["迅捷"],
            "normal": {
                "traitType": "作战能力",
                "traitDescription": "【拉特兰】每叠加5层，本干员攻击速度+1"
            },
            "elite": {
                "traitType": "作战能力",
                "traitDescription": "【拉特兰】每叠加5层，本干员攻击速度+2"
            }
        }
        """;

        var op = JsonSerializer.Deserialize<Operator>(json);

        op.Should().NotBeNull();
        op!.Name.Should().Be("隐现");
        op.Tier.Should().Be(6);
        op.CoreCovenant.Should().Be("拉特兰");
        op.AdditionalCovenants.Should().ContainSingle("迅捷");
        op.Normal.TraitType.Should().Be("作战能力");
        op.Normal.TraitDescription.Should().Contain("攻击速度+1");
        op.Elite.TraitDescription.Should().Contain("攻击速度+2");
    }

    [Fact]
    public void Deserialize_MultipleCovenants_AllPresent()
    {
        var json = """
        {
            "name": "角峰",
            "tier": 4,
            "coreCovenant": "谢拉格",
            "additionalCovenants": ["坚守"],
            "normal": { "traitType": "单次叠加", "traitDescription": "层数+2" },
            "elite": { "traitType": "单次叠加", "traitDescription": "层数+4" }
        }
        """;

        var op = JsonSerializer.Deserialize<Operator>(json);

        op!.Name.Should().Be("角峰");
        op.CoreCovenant.Should().Be("谢拉格");
        op.AdditionalCovenants.Should().Contain("坚守");
    }
}
