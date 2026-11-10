using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using ASPAssistant.App.Controls;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public class LineupDescriptionTests
{
    [Fact]
    public void SplitDescription_SeparatesLegacyDiagramsAndKeepsProseCodeExamples()
    {
        const string prose = "先上场\n\n```text\n一般示例\n```";
        const string diagram = "```mermaid\nA[{{op:地灵}}]\n```";
        var (introduction, source) = LineupDiagramSource.SplitDescription(prose + "\n\n" + diagram);
        introduction.Should().Be(prose);
        source.Should().Be(diagram);
        LineupDiagramSource.CombineDescription(introduction, source).Should().Be(prose + "\n\n" + diagram);
        LineupDiagramSource.CombineDescription(prose, "flowchart TD\nA[{{op:地灵}}]")
            .Should().Be(prose + "\n\n```lineup\nflowchart TD\nA[{{op:地灵}}]\n```");
    }

    [Fact]
    public void DiagramPreview_IgnoresLegacyPositions_AndSerializationDropsThem()
    {
        var source = "阵容介绍\r\n```text\r\n示例\r\n```\r\n\r\n" +
            "```mermaid\r\nflowchart LR\r\n%% 注释\r\nA[{{op:地灵}}]\r\n%% asp-pos A 255 98\r\n```\r\n结束";
        var block = LineupDiagramSource.FindBlocks(source, out var unclosed).Single();
        unclosed.Should().BeFalse();
        source[..block.Start].Should().EndWith("```\r\n\r\n");
        source[block.End..].Should().Be("\r\n结束");
        LineupDiagram.TryParse(block.Lines, out var graph, out _).Should().BeTrue();
        var generated = LineupDiagramSource.Format(graph, block.Kind, "\r\n");
        generated.Should().Be("```mermaid\r\nflowchart LR\r\n%% 注释\r\nA[{{op:地灵}}]\r\n```");
        LineupPerformanceTests.RunSta(() =>
        {
            Point GetNodePosition(string markdown)
            {
                var document = LineupMarkdown.Render(markdown, (_, _) => null);
                var scroll = (ScrollViewer)document.Blocks.OfType<BlockUIContainer>().Single().Child;
                var node = ((Canvas)scroll.Content).Children.OfType<Border>().Single();
                return new Point(Canvas.GetLeft(node), Canvas.GetTop(node));
            }

            GetNodePosition(source).Should().Be(GetNodePosition(generated));
        });
    }

    [Fact]
    public void RemovingLegacyPositions_PreservesProseAndOtherCodeFencesVerbatim()
    {
        const string prefix = "阵容介绍\r\n%% asp-pos A 255 98\n```text\r\n%% asp-pos A 255 98\r\n```\r\n";
        const string diagramStart = "```lineup\nflowchart TD\r\nA[{{op:地灵}}]\n";
        const string suffix = "%% 备注\r\n```\r\n后续说明";
        var source = prefix + diagramStart + "  %% asp-pos A 255 98\r\n" + suffix;

        LineupDiagramSource.RemoveLegacyPositions(source).Should().Be(prefix + diagramStart + suffix);
    }

    [Fact]
    public void Preview_RendersListsFormattingCatalogIconsAndGraphAnnotations()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var source = """
                - **粗体** *斜体* <u>下划线</u> ==高亮== {{op:地灵}}
                2. {{gear:长剑}}

                ```lineup
                flowchart LR
                A[{{op:地灵}}]
                B[{{gear:长剑}}]
                A -->|装备| B
                highlight A
                note A: 核心干员
                ```
                """;
            var document = LineupMarkdown.Render(source, (equipment, _) => equipment ? "gear.png" : "op.png");
            var blocks = document.Blocks.ToArray();
            var bullet = (System.Windows.Documents.List)blocks[0];
            var inlines = ((Paragraph)bullet.ListItems.FirstListItem!.Blocks.FirstBlock!).Inlines;
            inlines.Should().Contain(i => i is Bold).And.Contain(i => i is Italic).And.Contain(i => i is Underline);
            inlines.OfType<Span>().Should().Contain(span => span.Background == Brushes.Goldenrod);
            DeferredIcon.GetPath(inlines.OfType<InlineUIContainer>().Single().Child).Should().Be("op.png");
            var numbered = (System.Windows.Documents.List)blocks[1];
            numbered.StartIndex.Should().Be(2);
            var scroll = (ScrollViewer)((BlockUIContainer)blocks[2]).Child;
            var graph = (Canvas)scroll.Content;
            graph.Children.OfType<Polyline>().Should().ContainSingle();
            graph.Children.OfType<TextBlock>().Should().ContainSingle(label => label.Text == "装备");
            var core = graph.Children.OfType<Border>().Single(node => node.BorderBrush == Brushes.Gold);
            ((StackPanel)core.Child).Children.OfType<TextBlock>().Should().Contain(note => note.Text == "核心干员");
            var icons = graph.Children.OfType<Border>().SelectMany(node => ((StackPanel)node.Child).Children.OfType<Image>());
            icons.Select(DeferredIcon.GetPath).Should().Equal("op.png", "gear.png");
        });
    }

    [Fact]
    public void Preview_ReportsUndefinedDiagramNodeAndKeepsLaterText()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var source = "```mermaid\nA[{{op:地灵}}]\nA --> B\n```\n\n后续说明";
            var document = LineupMarkdown.Render(source, (_, _) => null);
            var paragraphs = document.Blocks.OfType<Paragraph>().ToArray();
            paragraphs.Should().HaveCount(2);
            ((Run)paragraphs[0].Inlines.FirstInline!).Text.Should().Contain("B 未定义");
            ((Run)paragraphs[1].Inlines.FirstInline!).Text.Should().Be("后续说明");
        });
    }
}
