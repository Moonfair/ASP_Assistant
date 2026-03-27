using System.Text.Json;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Models;

public class EquipmentTests
{
    [Fact]
    public void Deserialize_ValidJson_ReturnsEquipment()
    {
        var json = """
        {
            "name": "不屈弹射器",
            "tier": 2,
            "normal": { "effectDescription": "再部署时间-30%，生命值-30%" },
            "elite": { "effectDescription": "再部署时间-50%，生命值-30%" }
        }
        """;

        var eq = JsonSerializer.Deserialize<Equipment>(json);

        eq.Should().NotBeNull();
        eq!.Name.Should().Be("不屈弹射器");
        eq.Tier.Should().Be(2);
        eq.Normal.EffectDescription.Should().Contain("-30%");
        eq.Elite.EffectDescription.Should().Contain("-50%");
    }
}
