using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ASPAssistant.App.Controls;

internal static partial class LineupDiagram
{
    internal const double CardWidth = 148;
    internal const double CardHeight = 118;
    private const double ColumnPitch = 176;
    private const double HorizontalPitch = 280;
    private const double RowPitch = 186;
    internal const int MaxNodes = 20;
    internal const int MaxEdges = 40;
    internal const int MaxDiagrams = 8;
    internal sealed record Node(string Id, bool Equipment, string Name);
    internal sealed record Edge(string From, string To, string Label);

    internal sealed class Graph
    {
        public Dictionary<string, Node> Nodes { get; } = new(StringComparer.Ordinal);
        public List<Edge> Edges { get; } = [];
        public HashSet<string> Highlights { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Notes { get; } = new(StringComparer.Ordinal);
        public List<string> Comments { get; } = [];
        public bool Horizontal { get; set; }

        public IEnumerable<string> ToLines()
        {
            yield return Horizontal ? "flowchart LR" : "flowchart TD";
            foreach (var comment in Comments) yield return comment;
            foreach (var node in Nodes.Values) yield return $"{node.Id}[{{{{{(node.Equipment ? "gear" : "op")}:{node.Name}}}}}]";
            foreach (var edge in Edges)
                yield return edge.Label.Length == 0 ? $"{edge.From} --> {edge.To}" : $"{edge.From} -->|{edge.Label}| {edge.To}";
            foreach (var id in Nodes.Keys)
            {
                if (Highlights.Contains(id)) yield return $"highlight {id}";
                if (Notes.TryGetValue(id, out var note)) yield return $"note {id}: {note}";
            }
        }
    }

    internal static bool TryBuild(string[] lines, Func<bool, string, int, FrameworkElement> makeIcon,
        out FrameworkElement? diagram, out string error)
    {
        diagram = null;
        if (!TryParse(lines, out var graph, out error)) return false;
        if (graph.Nodes.Count == 0) return true;
        diagram = new ScrollViewer
        {
            Content = BuildCanvas(graph, makeIcon), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 430
        };
        return true;
    }

    internal static bool TryParse(string[] lines, out Graph graph, out string error)
    {
        graph = new Graph();
        var references = new List<(string Id, int Line)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("%%", StringComparison.Ordinal))
            {
                if (!LineupDiagramSource.IsLegacyPositionLine(line)) graph.Comments.Add(line);
                continue;
            }
            error = $"关系图第 {i + 1} 行";
            if (line is "flowchart TD" or "flowchart TB" or "graph TD" or "graph TB" or "flowchart LR" or "graph LR")
            {
                graph.Horizontal = line.EndsWith("LR", StringComparison.Ordinal);
                continue;
            }
            var match = NodePattern().Match(line);
            if (match.Success)
            {
                var id = match.Groups[1].Value;
                if (graph.Nodes.Count >= MaxNodes || !graph.Nodes.TryAdd(id, new Node(id, match.Groups[2].Value == "gear", match.Groups[3].Value.Trim())))
                {
                    error += "：节点编号重复，或超过 20 个节点。";
                    return false;
                }
                continue;
            }
            match = EdgePattern().Match(line);
            if (match.Success)
            {
                if (graph.Edges.Count >= MaxEdges)
                {
                    error += "：每张图最多支持 40 条连线。";
                    return false;
                }
                var edge = new Edge(match.Groups[1].Value, match.Groups[3].Value, match.Groups[2].Value.Trim());
                graph.Edges.Add(edge);
                references.Add((edge.From, i + 1));
                references.Add((edge.To, i + 1));
                continue;
            }
            match = HighlightPattern().Match(line);
            if (match.Success)
            {
                var id = match.Groups[1].Value;
                graph.Highlights.Add(id);
                references.Add((id, i + 1));
                continue;
            }
            match = NotePattern().Match(line);
            if (match.Success)
            {
                var id = match.Groups[1].Value;
                graph.Notes[id] = match.Groups[2].Value.Trim();
                references.Add((id, i + 1));
                continue;
            }
            error += "：无法识别，请检查节点、连线或注释的写法。";
            return false;
        }
        foreach (var reference in references)
        {
            if (graph.Nodes.ContainsKey(reference.Id)) continue;
            error = $"关系图第 {reference.Line} 行：节点 {reference.Id} 未定义。";
            return false;
        }
        error = "";
        return true;
    }

    internal static Canvas BuildCanvas(Graph graph, Func<bool, string, int, FrameworkElement> makeIcon,
        Action<Border, Node, Action<Point>>? configureNode = null, Action<UIElement, int>? configureEdge = null,
        IReadOnlyDictionary<string, Point>? manualPositions = null)
    {
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(25, 25, 46)) };
        if (graph.Nodes.Count == 0) return canvas;
        var horizontal = graph.Horizontal;
        var positions = Arrange(graph.Nodes.Keys, graph.Edges, horizontal);
        if (manualPositions is { Count: > 0 })
        {
            var occupied = new List<Rect>();
            foreach (var (id, position) in manualPositions)
            {
                if (!positions.ContainsKey(id)) continue;
                positions[id] = position;
                occupied.Add(new Rect(position, new Size(CardWidth, CardHeight)));
            }
            var step = horizontal ? new Vector(0, RowPitch) : new Vector(ColumnPitch, 0);
            foreach (var id in graph.Nodes.Keys.Where(id => !manualPositions.ContainsKey(id)))
            {
                var bounds = new Rect(positions[id], new Size(CardWidth, CardHeight));
                while (occupied.Any(rect => rect.IntersectsWith(bounds))) bounds.Offset(step);
                positions[id] = bounds.Location;
                occupied.Add(bounds);
            }
        }
        var width = positions.Values.Max(p => p.X) + CardWidth;
        var height = positions.Values.Max(p => p.Y) + CardHeight;
        var edgeUpdates = new List<(Edge Edge, double Lane, Action<Point[]> Update)>();
        var outside = 0;
        for (var index = 0; index < graph.Edges.Count; index++)
        {
            var edge = graph.Edges[index];
            var lane = (horizontal ? height : width) + 24 + outside * 18;
            var points = RouteEdge(positions[edge.From], positions[edge.To], horizontal, IsManual(edge), lane);
            if (points.Length > 2) outside++;
            var update = DrawEdge(canvas, points, edge.Label, index, configureEdge);
            if (configureNode is not null) edgeUpdates.Add((edge, lane, update));
        }
        canvas.Width = width + 18 + (!horizontal && outside > 0 ? 78 + outside * 18 : 0);
        canvas.Height = height + 18 + (horizontal && outside > 0 ? 34 + outside * 18 : 0);

        foreach (var node in graph.Nodes.Values)
        {
            var content = new StackPanel();
            var icon = makeIcon(node.Equipment, node.Name, 44);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(icon);
            content.Children.Add(new TextBlock
            {
                Text = node.Name, Foreground = Brushes.WhiteSmoke, FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = node.Name
            });
            if (graph.Notes.TryGetValue(node.Id, out var note))
                content.Children.Add(new TextBlock
                {
                    Text = note, Foreground = Brushes.LightSteelBlue, FontSize = 11,
                    TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 40, ToolTip = note
                });
            var highlighted = graph.Highlights.Contains(node.Id);
            var border = new Border
            {
                Width = CardWidth, Height = CardHeight, Padding = new Thickness(5), CornerRadius = new CornerRadius(6),
                BorderBrush = highlighted ? Brushes.Gold : Brushes.SlateGray,
                BorderThickness = new Thickness(highlighted ? 2 : 1), ClipToBounds = true,
                Background = new SolidColorBrush(highlighted ? Color.FromRgb(64, 53, 39) : Color.FromRgb(36, 36, 62)),
                Child = content
            };
            var origin = positions[node.Id];
            if (configureNode is not null)
            {
                var incidentEdges = edgeUpdates.Where(item => item.Edge.From == node.Id || item.Edge.To == node.Id).ToArray();
                configureNode(border, node, position =>
                {
                    Canvas.SetLeft(border, position.X);
                    Canvas.SetTop(border, position.Y);
                    foreach (var (edge, lane, update) in incidentEdges)
                    {
                        var from = edge.From == node.Id ? position : positions[edge.From];
                        var to = edge.To == node.Id ? position : positions[edge.To];
                        update(RouteEdge(from, to, horizontal, position != origin || IsManual(edge), lane));
                    }
                });
            }
            Put(canvas, border, origin.X, origin.Y);
        }
        return canvas;

        bool IsManual(Edge edge) => manualPositions?.ContainsKey(edge.From) == true || manualPositions?.ContainsKey(edge.To) == true;
    }

    private static Point[] RouteEdge(Point from, Point to, bool horizontal, bool manual, double lane)
    {
        var delta = to - from;
        if (manual && (Math.Abs(delta.X) > CardWidth || Math.Abs(delta.Y) > CardHeight))
        {
            var center = new Vector(CardWidth / 2, CardHeight / 2);
            var radius = 0.5 / Math.Max(Math.Abs(delta.X) / CardWidth, Math.Abs(delta.Y) / CardHeight);
            return [from + center + delta * radius, to + center - delta * radius];
        }
        if (horizontal && delta.X == HorizontalPitch)
            return [new(from.X + CardWidth, from.Y + CardHeight / 2), new(to.X, to.Y + CardHeight / 2)];
        if (!horizontal && delta.Y == RowPitch)
            return [new(from.X + CardWidth / 2, from.Y + CardHeight), new(to.X + CardWidth / 2, to.Y)];
        if (horizontal)
            return [new(from.X + CardWidth, from.Y + CardHeight / 2), new(from.X + CardWidth + 8, from.Y + CardHeight / 2),
                new(from.X + CardWidth + 8, lane), new(to.X - 8, lane), new(to.X - 8, to.Y + CardHeight / 2), new(to.X, to.Y + CardHeight / 2)];
        return [new(from.X + CardWidth / 2, from.Y + CardHeight), new(from.X + CardWidth / 2, from.Y + CardHeight + 10),
            new(lane, from.Y + CardHeight + 10), new(lane, to.Y - 10), new(to.X + CardWidth / 2, to.Y - 10), new(to.X + CardWidth / 2, to.Y)];
    }

    private static Dictionary<string, Point> Arrange(IEnumerable<string> ids, List<Edge> edges, bool horizontal)
    {
        var levels = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var pending = levels.Keys.ToHashSet(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            // Break a cycle at its first declared node. Its return edge uses an outside lane.
            var id = levels.Keys.FirstOrDefault(id => pending.Contains(id) &&
                !edges.Any(edge => edge.To == id && pending.Contains(edge.From)))
                ?? levels.Keys.First(pending.Contains);
            pending.Remove(id);
            foreach (var edge in edges.Where(edge => edge.From == id && pending.Contains(edge.To)))
                levels[edge.To] = Math.Max(levels[edge.To], levels[id] + 1);
        }
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        foreach (var level in levels.GroupBy(pair => pair.Value))
        {
            var index = 0;
            foreach (var node in level)
            {
                positions[node.Key] = horizontal
                    ? new Point(18 + level.Key * HorizontalPitch, 18 + index * RowPitch)
                    : new Point(18 + index * ColumnPitch, 18 + level.Key * RowPitch);
                index++;
            }
        }
        return positions;
    }

    private static Action<Point[]> DrawEdge(Canvas canvas, Point[] points, string label, int index, Action<UIElement, int>? configureEdge)
    {
        var line = new Polyline { Stroke = Brushes.LightSkyBlue, StrokeThickness = 1.5 };
        canvas.Children.Add(line);
        Polyline? hitTarget = null;
        if (configureEdge is not null)
        {
            hitTarget = new Polyline { Stroke = Brushes.Transparent, StrokeThickness = 12 };
            configureEdge(hitTarget, index);
            canvas.Children.Add(hitTarget);
        }
        var arrow = new Polygon { Fill = Brushes.LightSkyBlue, IsHitTestVisible = false };
        canvas.Children.Add(arrow);
        TextBlock? text = null;
        if (label.Length > 0)
        {
            text = new TextBlock
            {
                Text = label, Foreground = Brushes.WhiteSmoke, FontSize = 11, ToolTip = label,
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 46)), MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            configureEdge?.Invoke(text, index);
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            canvas.Children.Add(text);
        }
        Update(points);
        return Update;

        void Update(Point[] route)
        {
            var linePoints = new PointCollection(route);
            linePoints.Freeze();
            line.Points = linePoints;
            if (hitTarget is not null) hitTarget.Points = linePoints;
            var tip = route[^1];
            var direction = tip - route[^2];
            direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            var tail = tip - direction * 8;
            arrow.Points = new PointCollection([tip, tail + normal * 4, tail - normal * 4]);
            if (text is null) return;
            var a = route[(route.Length - 1) / 2];
            var b = route[route.Length / 2];
            Canvas.SetLeft(text, Math.Max(2, (a.X + b.X - text.DesiredSize.Width) / 2));
            Canvas.SetTop(text, Math.Max(2, (a.Y + b.Y - text.DesiredSize.Height) / 2));
        }
    }

    private static void Put(Canvas canvas, UIElement child, double x, double y)
    {
        Canvas.SetLeft(child, x);
        Canvas.SetTop(child, y);
        canvas.Children.Add(child);
    }

    private const string Id = @"([A-Za-z_][A-Za-z_0-9]{0,23})";
    [GeneratedRegex("^" + Id + @"\s*\[\s*\{\{(op|gear):([^{}]{1,100})\}\}\s*\]$")]
    private static partial Regex NodePattern();
    [GeneratedRegex("^" + Id + @"\s*-->\s*(?:\|([^|]{0,40})\|\s*)?" + Id + "$")]
    private static partial Regex EdgePattern();
    [GeneratedRegex(@"^highlight\s+" + Id + "$")]
    private static partial Regex HighlightPattern();
    [GeneratedRegex(@"^note\s+" + Id + @"\s*:\s*(.{1,60})$")]
    private static partial Regex NotePattern();
}
