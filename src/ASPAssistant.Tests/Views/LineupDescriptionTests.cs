using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class LineupDescriptionTests
{
    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD/", "原视频", "在哔哩哔哩打开")]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=12345", "原攻略", "在森空岛打开")]
    public void Preview_RendersSourceAsAttributionAndDirectLink(string url, string heading, string linkText)
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var source = "阵容介绍\n\n```aspa-source\n原作者 **昵称**\n" + url +
                "\n```\n\n```lineup\nA[{{op:地灵}}]\n```";
            var blocks = LineupMarkdown.Render(source, (_, _) => null).Blocks.ToArray();
            ((Paragraph)blocks[0]).Inlines.OfType<Run>().Single().Text.Should().Be("阵容介绍");
            var attribution = (Section)blocks[1];
            new TextRange(attribution.ContentStart, attribution.ContentEnd).Text.Should().Contain(heading)
                .And.Contain("原作者 **昵称**");
            var link = attribution.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines.OfType<Hyperlink>())
                .Should().ContainSingle().Which;
            link.NavigateUri.AbsoluteUri.Should().Be(url);
            new TextRange(link.ContentStart, link.ContentEnd).Text.Should().Be(linkText);
            blocks[2].Should().BeOfType<BlockUIContainer>();
        });
    }

    [Fact]
    public void WidePreview_PutsAttributionInTheIntroductionAndDiagramInTheRightPane()
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var preview = new LineupPreviewView();
            LineupPerformanceTests.Layout(preview);
            preview.ShowLineup(new Lineup
            {
                Description = "阵容说明\n\n```aspa-source\n作者\nhttps://b23.tv/Example1\n```\n\n```lineup\nA[{{op:地灵}}]\n```"
            }, (_, _) => null);
            var introduction = (FlowDocumentScrollViewer)preview.FindName("Introduction");
            introduction.Document.Blocks.OfType<Section>().Should().ContainSingle();
            introduction.Document.Blocks.OfType<BlockUIContainer>().Should().BeEmpty();
            var diagram = (FlowDocumentScrollViewer)preview.FindName("DiagramPreview");
            diagram.Document.Blocks.OfType<BlockUIContainer>().Should().ContainSingle();

            preview.Measure(new Size(620, 730));
            preview.Arrange(new Rect(0, 0, 620, 730));
            preview.UpdateLayout();
            introduction.Document.Blocks.OfType<Section>().Should().ContainSingle();
            introduction.Document.Blocks.OfType<BlockUIContainer>().Should().ContainSingle();
            diagram.Document.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(1020, "DiagramPreview")]
    [InlineData(620, "Introduction")]
    public void Preview_WheelOverDiagramZoomsInAndOut(int width, string viewerName)
    {
        LineupPerformanceTests.RunSta(() =>
        {
            var preview = new LineupPreviewView();
            preview.ShowLineup(new Lineup
            {
                Description = "```lineup\nflowchart LR\nA[{{op:地灵}}]\nB[{{gear:长剑}}]\nA --> B\n```"
            }, (_, _) => null);
            preview.Measure(new Size(width, 730));
            preview.Arrange(new Rect(0, 0, width, 730));
            preview.UpdateLayout();

            var viewer = (FlowDocumentScrollViewer)preview.FindName(viewerName);
            var surface = (ScrollViewer)viewer.Document.Blocks.OfType<BlockUIContainer>().Single().Child;
            var canvas = (Canvas)surface.Content;
            var node = canvas.Children.OfType<Border>().First();
            var initialWidth = surface.ExtentWidth;
            initialWidth.Should().BeGreaterThan(0);

            ScrollWheel(120);
            surface.ExtentWidth.Should().BeGreaterThan(initialWidth);
            ScrollWheel(-120);
            surface.ExtentWidth.Should().BeApproximately(initialWidth, 1);

            void ScrollWheel(int delta)
            {
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                {
                    RoutedEvent = Mouse.PreviewMouseWheelEvent
                };
                node.RaiseEvent(wheel);
                wheel.Handled.Should().BeTrue("the wheel should zoom the diagram in place");
                preview.UpdateLayout();
            }
        });
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD/")]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=12345")]
    public void Editor_EditsAndSavesSourceSeparatelyFromLineupAuthorAndIntroduction(string sourceUrl)
    {
        LineupPerformanceTests.RunSta(() =>
        {
            const string intro = "阵容介绍";
            const string graph = "```lineup\nA[{{op:地灵}}]\n```";
            var legacy = new Lineup
            {
                Name = "阵容", Author = "整理者", Members = [new() { OperatorName = "地灵" }],
                Description = intro + "\n\n" + graph
            };
            var vm = new LineupEditorViewModel([new() { Name = "地灵" }], [], legacy);
            Lineup? saved = null;
            var window = new LineupEditorWindow(vm, result =>
            {
                saved = result;
                return Task.FromResult(true);
            });
            try
            {
                ((Button)window.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var workspace = (LineupDescriptionView)window.FindName("DescriptionWorkspace");
                var author = (TextBox)workspace.FindName("SourceVideoAuthor");
                var url = (TextBox)workspace.FindName("SourceVideoUrl");
                var open = (Button)workspace.FindName("OpenSourceVideoButton");
                author.Text.Should().BeEmpty();
                url.Text.Should().BeEmpty();
                open.IsEnabled.Should().BeFalse();

                author.Text = "原作者";
                url.Text = sourceUrl;
                open.IsEnabled.Should().BeTrue();
                ((TextBox)workspace.FindName("IntroductionEditor")).Text.Should().Be(intro);
                ((TextBox)workspace.FindName("SourceEditor")).Text.Should().Be(graph);
                ((Button)window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                saved.Should().NotBeNull();
                saved!.Author.Should().Be("整理者");
                saved.Description.Should().Be(intro + "\n\n```aspa-source\n原作者\n" + url.Text + "\n```\n\n" + graph);

                var reopened = new LineupDescriptionView
                {
                    DataContext = new LineupEditorViewModel([new() { Name = "地灵" }], [], saved)
                };
                ((TextBox)reopened.FindName("SourceVideoAuthor")).Text.Should().Be(author.Text);
                ((TextBox)reopened.FindName("SourceVideoUrl")).Text.Should().Be(url.Text);
            }
            finally
            {
                window.ApprovePanelClose();
                window.Close();
            }
        });
    }

    [Fact]
    public void VideoAttribution_RoundTripsAfterIntroductionAndBeforeDiagram()
    {
        const string introduction = "先上场\n\n````text\n```aspa-source\n示例作者\n示例链接\n```\n````";
        const string diagram = "```lineup\nA[{{op:地灵}}]\n```";
        const string attribution = "```aspa-source\n原作者\nhttps://b23.tv/Example1\n```";
        var description = introduction + "\n\n" + attribution + "\n\n" + diagram;
        var parts = LineupSourceVideo.SplitDescription(description);
        parts.Introduction.Should().Be(introduction);
        parts.Diagram.Should().Be(diagram);
        parts.Video.Should().Be(new LineupSourceVideo("原作者", "https://b23.tv/Example1"));
        LineupSourceVideo.CombineDescription(parts.Introduction, parts.Diagram, parts.Video).Should().Be(description);
        LineupSourceVideo.CombineDescription(parts.Introduction, parts.Diagram, new LineupSourceVideo("", ""))
            .Should().Be(introduction + "\n\n" + diagram);
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD/?p=2#t=30", true)]
    [InlineData("https://m.bilibili.com:443/video/av42", true)]
    [InlineData("https://b23.tv/Example1", true)]
    [InlineData("https://www.bilibili.com@b23.tv/Example1", false)]
    [InlineData("https://b23.tv:444/Example1", false)]
    [InlineData("https://bilibili.com/video/other/../av42", false)]
    [InlineData("https://www.bilibili.com.evil.example/video/BV1xx411c7mD/", false)]
    [InlineData("https://www.bilibili.com/space/123", false)]
    [InlineData("http://b23.tv/Example1", false)]
    public void VideoAttribution_OnlyEnablesDirectHttpsVideoLinks(string url, bool valid)
    {
        (new LineupSourceVideo("原作者", url).BrowserUri is not null).Should().Be(valid);
    }

    [Theory]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=12345", true)]
    [InlineData("https://game.skland.com:443/arknights/stronghold-guide/detail?utm_source=share&guideId=12#top", true)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=%31%32", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?%67uideId=123", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=123&guideId=456", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=123&%67uideId=456", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=１２３", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=1+2", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?other=123", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail/?guideId=123", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/other/../detail?guideId=123", false)]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail#?guideId=123", false)]
    [InlineData("https://game.skland.com.evil.example/arknights/stronghold-guide/detail?guideId=123", false)]
    [InlineData("https://someone@game.skland.com/arknights/stronghold-guide/detail?guideId=123", false)]
    [InlineData("https://game.skland.com:444/arknights/stronghold-guide/detail?guideId=123", false)]
    [InlineData("http://game.skland.com/arknights/stronghold-guide/detail?guideId=123", false)]
    public void SklandAttribution_RequiresExactHttpsGuidePathAndOneNumericId(string url, bool valid)
    {
        (new LineupSourceVideo("原作者", url).BrowserUri is not null).Should().Be(valid);
    }

    [Fact]
    public void SharedDiagram_RestoresManualCoordinatesInDesktopPreview()
    {
        var source = string.Join("\n", "```lineup", "flowchart LR", "A[{{op:地灵}}]", "B[{{gear:长剑}}]",
            "A -->|装备| B", "highlight A", "note A: 核心", "%% aspa-layout A 211 88", "%% aspa-layout B 419 132", "```");
        var block = LineupDiagramSource.FindBlocks(source, out _).Single();
        LineupDiagram.TryParse(block.Lines, out var graph, out var error).Should().BeTrue(error);
        LineupDiagramSource.Format(graph, block.Kind, "\n").Should().Be(source);
        LineupPerformanceTests.RunSta(() =>
        {
            var document = LineupMarkdown.Render(source, (_, _) => null);
            var scroll = (ScrollViewer)document.Blocks.OfType<BlockUIContainer>().Single().Child;
            var cards = ((Canvas)scroll.Content).Children.OfType<Border>().ToArray();
            new Point(Canvas.GetLeft(cards[0]), Canvas.GetTop(cards[0])).Should().Be(new Point(211, 88));
            new Point(Canvas.GetLeft(cards[1]), Canvas.GetTop(cards[1])).Should().Be(new Point(419, 132));
        });
    }

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
