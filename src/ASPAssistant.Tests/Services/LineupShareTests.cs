using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class LineupShareTests
{
    private readonly LineupShareService _service = new(["能天使", "阿米娅"], ["长剑", "法杖"]);

    public static Lineup Example() => new()
    {
        Name = "双核阵容", Author = "博士 🐱",
        Members = [new() { OperatorName = "能天使", IsStackingCore = true, IsDamageCore = true,
            Equipment1 = "长剑", Equipment2 = "长剑" }, new() { OperatorName = "阿米娅" }]
    };

    [Fact]
    public void ExportImport_PreservesAuthorRolesAndBothSlots_ButCreatesNewIdentity()
    {
        var source = Example();
        var text = _service.Export(source);
        text.Should().StartWith("ASPA-LINEUP/1\n# ASP Assistant · 明日方舟「卫戍协议」实时辅助工具 · 官网 https://aspa.moonfair.cn\n");
        text.Should().Contain("博士").And.Contain("能天使");
        var imported = _service.Import("\r\n" + text.Replace("\n", "\r\n") + "\n ");
        imported.Should().BeEquivalentTo(source with { IsImported = true }, o => o.Excluding(x => x.Id));
        text.Should().NotContain("IsImported");
        imported.Id.Should().NotBe(source.Id);
    }

    [Fact]
    public void RichDescription_RoundTripsMarkdownAndDiagram_WithinShareLimit()
    {
        var description = "- **输出** {{op:能天使}}\n```lineup\nA[{{op:能天使}}]\nnote A: 主力\n```";
        var lineup = Example() with { Description = description };
        var exported = _service.Export(lineup);
        exported.Should().StartWith("ASPA-LINEUP/2\n# ASP Assistant · 明日方舟「卫戍协议」实时辅助工具 · 官网 https://aspa.moonfair.cn\n");
        _service.Import(exported).Description.Should().Be(description);
        var full = Example() with { Description = new string('\u0001', Lineup.MaxDescriptionLength) };
        _service.Import(_service.Export(full)).Description.Should().Be(full.Description);
        var oversized = lineup with { Description = new string('x', Lineup.MaxDescriptionLength + 1) };
        ((Action)(() => _service.Export(oversized))).Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("ASPA-LINEUP/1")]
    [InlineData("ASPA-LINEUP/2")]
    public void Import_AcceptsLegacyShareWithoutComment(string header)
    {
        var lineup = _service.Import(header + "\n{\"Name\":\"旧阵容\",\"Author\":\"博士\",\"Members\":[{\"OperatorName\":\"能天使\"}]}");
        lineup.Name.Should().Be("旧阵容");
        lineup.Members.Should().ContainSingle(member => member.OperatorName == "能天使");
        lineup.AvailableStrategies.Should().BeEmpty();
    }

    [Fact]
    public void ExportImport_PreservesAvailableStrategiesWithoutDescription()
    {
        var source = Example() with { AvailableStrategies = ["众志合一", "旧赛季策略"] };
        var share = _service.Export(source);
        share.Should().StartWith("ASPA-LINEUP/2\n");
        _service.Import(share).AvailableStrategies.Should().Equal(source.AvailableStrategies);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a lineup")]
    [InlineData("ASPA-LINEUP/2\n{}")]
    [InlineData("ASPA-LINEUP/1\nnull")]
    [InlineData("ASPA-LINEUP/1\n{\"Name\":\"a\",\"Author\":\"b\",\"Members\":null}")]
    [InlineData("ASPA-LINEUP/1\n{\"Name\":\"a\",\"Author\":\"b\",\"Members\":[null]}")]
    [InlineData("ASPA-LINEUP/1\n{\"Name\":\"a\",\"Author\":\"b\",\"Members\":[{\"OperatorName\":\"能天使\",\"IsDamageCore\":\"yes\"}]}")]
    [InlineData("ASPA-LINEUP/2\n{\"Name\":\"a\",\"Author\":\"b\",\"Members\":[{\"OperatorName\":\"能天使\"}],\"AvailableStrategies\":[\"众志合一\",\"众志合一\"]}")]
    public void Import_RejectsMalformedOrUnsupportedText(string text)
        => ((Action)(() => _service.Import(text))).Should().Throw<FormatException>();

    [Fact]
    public void Validation_RejectsDuplicateUnknownAndOversizedMembers()
    {
        var source = Example();
        var invalid = new[]
        {
            source with { Members = [source.Members[0], source.Members[0]] },
            source with { Members = [new() { OperatorName = "不存在" }] },
            source with { Members = [new() { OperatorName = "能天使", Equipment2 = "不存在" }] },
            source with { Members = [] },
            source with { Name = " " }, source with { Author = " " },
            source with { Members = Enumerable.Range(0, 11).Select(i => new LineupMember { OperatorName = $"干员{i}" }).ToList() }
        };
        foreach (var item in invalid)
            ((Action)(() => _service.Export(item))).Should().Throw<FormatException>();
        ((Action)(() => _service.Import(new string('x', 100_001)))).Should().Throw<FormatException>();
    }

    [Fact]
    public void Import_EnforcesCatalogAndDefaultSlots_AndAcceptsThirtyMembersWithTags()
    {
        var source = _service.Export(Example());
        foreach (var text in new[]
        {
            source.Replace("能天使", "未知干员"), source.Replace("长剑", "未知装备"),
            source.Replace("\"IsDamageCore\": true", "\"IsDamageCore\": true, \"Equipment3\": \"长剑\"")
        })
            ((Action)(() => _service.Import(text))).Should().Throw<FormatException>();

        var names = Enumerable.Range(0, 31).Select(i => $"干员{i}").ToList();
        var codec = new LineupShareService(names, []);
        var lineup = Example() with { Members = names.Take(30).Select(n => new LineupMember { OperatorName = n }).ToList() };
        var share = codec.Export(lineup);
        codec.Import(share).Members.Should().HaveCount(30);
        var tagged = share.Insert(share.LastIndexOf('}'), ", \"CovenantTags\": [\"炎\", \"迅捷\"]");
        codec.Export(codec.Import(tagged)).Should().Contain("\"CovenantTags\"").And.Contain("\"迅捷\"");
        lineup.Members.Add(new() { OperatorName = names.Last() });
        ((Action)(() => codec.Export(lineup))).Should().Throw<FormatException>();
    }
}
