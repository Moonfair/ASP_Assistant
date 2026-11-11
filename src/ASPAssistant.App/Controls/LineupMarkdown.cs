using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ASPAssistant.App.Controls;

internal sealed partial class LineupMarkdown(Func<bool, string, string?> findIcon, Func<string, string?>? findStrategyIcon)
{
    private readonly Dictionary<(string Kind, string Name), string?> _icons = new();
    private int _imageCount;

    internal static FlowDocument Render(string source, Func<bool, string, string?> findIcon,
        string emptyMessage = "在左侧写下阵容思路、装备搭配或操作顺序。",
        Func<string, string?>? findStrategyIcon = null, IReadOnlyList<string>? availableStrategies = null)
        => new LineupMarkdown(findIcon, findStrategyIcon).Build(source, emptyMessage, availableStrategies);

    private FlowDocument Build(string source, string emptyMessage, IReadOnlyList<string>? availableStrategies)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 14,
            Foreground = Brushes.WhiteSmoke, PagePadding = new Thickness(14)
        };
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        System.Windows.Documents.List? list = null;
        Paragraph? paragraph = null;
        var diagramCount = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                list = null;
                paragraph = null;
                var fence = line[3..].Trim();
                var start = i + 1;
                while (++i < lines.Length && lines[i].Trim() != "```") { }
                var body = lines[start..i];
                if (fence is "lineup" or "mermaid")
                {
                    if (i == lines.Length)
                        document.Blocks.Add(Message("关系图缺少结束标记 ```。"));
                    else if (++diagramCount > LineupDiagram.MaxDiagrams)
                        document.Blocks.Add(Message($"每篇介绍最多预览 {LineupDiagram.MaxDiagrams} 张关系图。"));
                    else if (LineupDiagram.TryBuild(body, (equipment, name, size) =>
                        MakeIcon(equipment ? "gear" : "op", name, size), out var diagram, out var error))
                    {
                        if (diagram is not null)
                            document.Blocks.Add(new BlockUIContainer(diagram) { Margin = new Thickness(0, 6, 0, 10) });
                    }
                    else
                        document.Blocks.Add(Message(error));
                }
                else
                {
                    document.Blocks.Add(new Paragraph(new Run(string.Join("\n", body)))
                    {
                        FontFamily = new FontFamily("Consolas"), Background = Brushes.DarkSlateBlue
                    });
                }
                continue;
            }
            if (line.Length == 0)
            {
                list = null;
                paragraph = null;
                continue;
            }
            var listItem = ListItemPattern().Match(line);
            if (listItem.Success)
            {
                paragraph = null;
                var ordered = listItem.Groups["number"].Success;
                var marker = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
                if (list is null || list.MarkerStyle != marker)
                {
                    list = new System.Windows.Documents.List { MarkerStyle = marker, Margin = new Thickness(0, 4, 0, 8) };
                    if (ordered) list.StartIndex = Math.Max(1, int.Parse(listItem.Groups["number"].Value));
                    document.Blocks.Add(list);
                }
                var item = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                RenderInline(item.Inlines, listItem.Groups["body"].Value);
                list.ListItems.Add(new ListItem(item));
                continue;
            }
            list = null;
            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                var title = new Paragraph
                {
                    FontSize = 23 - heading.Groups[1].Length * 2, FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 8, 0, 6)
                };
                RenderInline(title.Inlines, heading.Groups[2].Value);
                document.Blocks.Add(title);
                paragraph = null;
                continue;
            }
            if (paragraph is null)
            {
                paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 9) };
                document.Blocks.Add(paragraph);
            }
            else paragraph.Inlines.Add(new LineBreak());
            RenderInline(paragraph.Inlines, line);
        }
        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph(new Run(emptyMessage))
            {
                Foreground = Brushes.LightSlateGray
            });
        if (availableStrategies is { Count: > 0 })
        {
            var strategies = new Paragraph(new Bold(new Run("可用策略　"))) { Margin = new Thickness(0, 0, 0, 10) };
            for (var i = 0; i < availableStrategies.Count; i++)
            {
                var name = availableStrategies[i];
                if (i > 0) strategies.Inlines.Add(new Run(" · "));
                if (MakeImage("strategy", name, 23) is { } image)
                {
                    strategies.Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
                    strategies.Inlines.Add(new Run("\u00A0"));
                }
                strategies.Inlines.Add(new Run(name));
            }
            document.Blocks.InsertBefore(document.Blocks.FirstBlock, strategies);
        }
        return document;
    }

    private void RenderInline(InlineCollection inlines, string text, int depth = 0)
    {
        if (depth >= 8) { inlines.Add(new Run(text)); return; }
        var end = 0;
        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > end) inlines.Add(new Run(text[end..match.Index]));
            end = match.Index + match.Length;
            if (match.Groups["kind"].Success)
            {
                var name = match.Groups["name"].Value.Trim();
                var icon = MakeIcon(match.Groups["kind"].Value, name, 23);
                inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
                continue;
            }
            if (match.Groups["literal"].Success)
            {
                inlines.Add(new Run(match.Groups["literal"].Value));
                continue;
            }
            if (match.Groups["code"].Success)
            {
                inlines.Add(new Run(match.Groups["code"].Value) { FontFamily = new FontFamily("Consolas") });
                continue;
            }
            (Span span, string body) = match switch
            {
                _ when match.Groups["both"].Success => (new Span { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic }, "both"),
                _ when match.Groups["bold"].Success => (new Bold(), "bold"),
                _ when match.Groups["italic"].Success => (new Italic(), "italic"),
                _ when match.Groups["underline"].Success => (new Underline(), "underline"),
                _ => (new Span { Background = Brushes.Goldenrod, Foreground = Brushes.Black }, "mark")
            };
            RenderInline(span.Inlines, match.Groups[body].Value, depth + 1);
            inlines.Add(span);
        }
        if (end < text.Length) inlines.Add(new Run(text[end..]));
    }

    private FrameworkElement MakeIcon(string kind, string name, int size)
        => (FrameworkElement?)MakeImage(kind, name, size)
           ?? new TextBlock { Text = $"[{name}]", Foreground = Brushes.LightSkyBlue, ToolTip = name };

    private Image? MakeImage(string kind, string name, int size)
    {
        var key = (kind, name);
        if (!_icons.TryGetValue(key, out var path))
            _icons[key] = path = kind == "strategy" ? findStrategyIcon?.Invoke(name) : findIcon(kind == "gear", name);
        if (string.IsNullOrEmpty(path) || _imageCount >= 160)
            return null;

        _imageCount++;
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, ToolTip = name };
        AutomationProperties.SetName(image, name);
        DeferredIcon.SetPath(image, path);
        return image;
    }

    private static Paragraph Message(string text) => new(new Run(text)) { Foreground = Brushes.Gold };

    [GeneratedRegex(@"^(?:[-+*]|(?<number>[0-9]{1,6})[.)])\s+(?<body>.+)$")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"^(#{1,3})\s+(.+)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\\(?<literal>[\\*_`={<>])|`(?<code>[^`]+)`|\{\{(?<kind>op|gear|strategy):(?<name>[^{}]{1,100})\}\}|\*\*\*(?<both>.+?)\*\*\*|\*\*(?<bold>.+?)\*\*|__(?<bold>.+?)__|\*(?<italic>[^*]+)\*|_(?<italic>[^_]+)_|<u>(?<underline>.+?)</u>|==(?<mark>.+?)==")]
    private static partial Regex InlinePattern();
}
