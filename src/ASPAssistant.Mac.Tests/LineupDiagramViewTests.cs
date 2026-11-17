using ASPAssistant.Mac;

namespace ASPAssistant.Mac.Tests;

public class LineupDiagramViewTests
{
    [Fact]
    public void ParsesSharedWindowsDiagramWithLayoutAndEdges()
    {
        const string description = """
            使用方法

            ```lineup
            flowchart LR
            A[{{op:卡达}}]
            B[{{gear:源石溶剂}}]
            A -->|购买| B
            %% aspa-layout A 200 100
            ```
            """;
        var graph = Assert.Single(LineupDiagramView.Parse(description));
        Assert.True(graph.Horizontal);
        Assert.Collection(graph.Nodes,
            node => Assert.Equal(("A", false, "卡达"), (node.Id, node.Equipment, node.Name)),
            node => Assert.Equal(("B", true, "源石溶剂"), (node.Id, node.Equipment, node.Name)));
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(("A", "B", "购买"), (edge.From, edge.To, edge.Label));
        Assert.Equal(new Avalonia.Point(200, 100), graph.Positions["A"]);
    }
}
