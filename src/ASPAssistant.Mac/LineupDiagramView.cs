using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace ASPAssistant.Mac;

internal static class LineupDiagramView
{
    private static readonly Regex Fence = new(@"(?ms)^```(?:lineup|mermaid)[ \t]*\r?\n(?<body>.*?)^```[ \t]*$",
        RegexOptions.Compiled);
    private static readonly Regex Node = new(@"^([A-Za-z_][A-Za-z_0-9]{0,23})\s*\[\s*\{\{(op|gear):([^{}]{1,100})\}\}\s*\]$",
        RegexOptions.Compiled);
    private static readonly Regex Edge = new(@"^([A-Za-z_][A-Za-z_0-9]{0,23})\s*-->\s*(?:\|([^|]{0,40})\|\s*)?([A-Za-z_][A-Za-z_0-9]{0,23})$",
        RegexOptions.Compiled);
    private static readonly Regex Position = new(@"^%% aspa-layout\s+([A-Za-z_][A-Za-z_0-9]{0,23})\s+([0-9]{1,5})\s+([0-9]{1,5})$",
        RegexOptions.Compiled);

    internal sealed record DiagramNode(string Id, bool Equipment, string Name);
    internal sealed record DiagramEdge(string From, string To, string Label);
    internal sealed record DiagramGraph(IReadOnlyList<DiagramNode> Nodes,
        IReadOnlyList<DiagramEdge> Edges, IReadOnlyDictionary<string, Point> Positions, bool Horizontal);

    internal static IReadOnlyList<DiagramGraph> Parse(string description)
    {
        var graphs = new List<DiagramGraph>();
        foreach (Match fence in Fence.Matches(description))
        {
            var nodes = new List<DiagramNode>();
            var edges = new List<DiagramEdge>();
            var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
            bool horizontal = false;
            foreach (var raw in fence.Groups["body"].Value.Split('\n'))
            {
                var line = raw.Trim();
                if (line.EndsWith(" LR", StringComparison.Ordinal)) horizontal = true;
                var node = Node.Match(line);
                if (node.Success && nodes.Count < 20)
                    nodes.Add(new DiagramNode(node.Groups[1].Value,
                        node.Groups[2].Value == "gear", node.Groups[3].Value.Trim()));
                var edge = Edge.Match(line);
                if (edge.Success && edges.Count < 40)
                    edges.Add(new DiagramEdge(edge.Groups[1].Value, edge.Groups[3].Value,
                        edge.Groups[2].Value.Trim()));
                var position = Position.Match(line);
                if (position.Success && int.TryParse(position.Groups[2].Value, out int x) &&
                    int.TryParse(position.Groups[3].Value, out int y))
                    positions[position.Groups[1].Value] = new Point(x, y);
            }
            if (nodes.Count > 0) graphs.Add(new DiagramGraph(nodes, edges, positions, horizontal));
        }
        return graphs;
    }

    public static Control Create(string description, MacWorkspace workspace)
    {
        var root = new StackPanel { Spacing = 12 };
        var prose = Fence.Replace(description, "").Trim();
        if (prose.Length > 0)
            root.Children.Add(new TextBlock { Text = prose, TextWrapping = TextWrapping.Wrap });
        foreach (var graph in Parse(description))
        {
            var canvas = new Canvas { Background = new SolidColorBrush(Color.Parse("#19192E")) };
            var points = new Dictionary<string, Point>(StringComparer.Ordinal);
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                var node = graph.Nodes[i];
                var fallback = graph.Horizontal
                    ? new Point(20 + i * 180, 20)
                    : new Point(20 + i % 3 * 180, 20 + i / 3 * 130);
                points[node.Id] = graph.Positions.GetValueOrDefault(node.Id, fallback);
            }
            canvas.Width = Math.Max(500, points.Values.Max(point => point.X) + 170);
            canvas.Height = Math.Max(150, points.Values.Max(point => point.Y) + 120);
            foreach (var edge in graph.Edges)
            {
                if (!points.TryGetValue(edge.From, out var from) || !points.TryGetValue(edge.To, out var to)) continue;
                var start = new Point(from.X + 140, from.Y + 45);
                var end = new Point(to.X, to.Y + 45);
                if (!graph.Horizontal && to.Y > from.Y)
                {
                    start = new Point(from.X + 70, from.Y + 90);
                    end = new Point(to.X + 70, to.Y);
                }
                canvas.Children.Add(new Line { StartPoint = start, EndPoint = end,
                    Stroke = Brushes.LightSkyBlue, StrokeThickness = 2 });
                if (edge.Label.Length > 0)
                {
                    var label = new TextBlock { Text = edge.Label, Foreground = Brushes.LightSkyBlue,
                        Background = new SolidColorBrush(Color.Parse("#19192E")) };
                    Canvas.SetLeft(label, (start.X + end.X) / 2);
                    Canvas.SetTop(label, (start.Y + end.Y) / 2 - 18);
                    canvas.Children.Add(label);
                }
            }
            foreach (var node in graph.Nodes)
            {
                var card = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
                var icon = node.Equipment
                    ? workspace.Equipment.FirstOrDefault(item => item.Name == node.Name)?.IconPath
                    : workspace.Operators.FirstOrDefault(item => item.Name == node.Name)?.IconPath;
                card.Children.Add(AssetImages.Create(workspace.DataDirectory, icon, 44));
                card.Children.Add(new TextBlock { Text = node.Name, TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 132 });
                var border = new Border { Width = 140, Height = 90, Padding = new Thickness(5),
                    CornerRadius = new CornerRadius(5), BorderBrush = Brushes.SlateGray,
                    BorderThickness = new Thickness(1), Child = card };
                Canvas.SetLeft(border, points[node.Id].X);
                Canvas.SetTop(border, points[node.Id].Y);
                canvas.Children.Add(border);
            }
            root.Children.Add(new ScrollViewer { Content = canvas, Height = Math.Min(430, canvas.Height + 20) });
        }
        return root;
    }
}
