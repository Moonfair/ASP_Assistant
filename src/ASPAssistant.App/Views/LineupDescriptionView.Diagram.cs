using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

public partial class LineupDescriptionView
{
    private const double MaxDiagramCoordinate = 8000;
    private List<LineupDiagramSource.Block> _graphBlocks = [];
    private LineupDiagram.Graph? _graph;
    private string? _graphSource;
    private int _graphIndex;
    private bool _settingDiagramPicker;
    private string? _selectedNode;
    private int? _selectedEdge;
    private string? _connectFrom;
    private Line? _connectionPreview;
    private Canvas? _canvas;
    private readonly TranslateTransform _diagramPan = new();
    private Point? _panStart;
    private Vector _panOffset;
    private Dictionary<string, Point>? CurrentDraggedPositions => _graph?.Positions;
    private FrameworkElement DiagramViewport =>
        DiagramSurface.Template?.FindName("PART_ScrollContentPresenter", DiagramSurface) as FrameworkElement ?? DiagramSurface;

    private void RenderDiagram(bool keepSelection = false, bool revealNode = false)
    {
        var source = SourceEditor.Text;
        if (!keepSelection && !string.Equals(source, _graphSource, StringComparison.Ordinal))
        {
            ClearGraphSelection();
            ResetDiagramView();
        }
        _graphSource = source;
        _graphBlocks = LineupDiagramSource.FindEditorBlocks(source, out var hasUnclosedFence)
            .Take(LineupDiagram.MaxDiagrams).ToList();
        _graphIndex = Math.Clamp(_graphIndex, 0, Math.Max(0, _graphBlocks.Count - 1));
        _settingDiagramPicker = true;
        if (DiagramPicker.Items.Count != _graphBlocks.Count)
            DiagramPicker.ItemsSource = Enumerable.Range(1, _graphBlocks.Count).Select(index => $"流程图 {index}").ToArray();
        DiagramPicker.SelectedIndex = _graphBlocks.Count == 0 ? -1 : _graphIndex;
        DiagramPicker.Visibility = _graphBlocks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _settingDiagramPicker = false;
        _canvas = null;
        _connectionPreview = null;

        var block = _graphBlocks.ElementAtOrDefault(_graphIndex);
        var error = "";
        if (block is null && hasUnclosedFence)
        {
            _graph = null;
            error = "左侧有未结束的代码块。请先补上结束标记，再添加流程图。";
        }
        else if (block is null && !string.IsNullOrWhiteSpace(source))
        {
            _graph = null;
            error = "请在流程图源码里输入图定义，或使用图模板。";
        }
        else if (block is null)
            _graph = new LineupDiagram.Graph();
        else if (!block.Closed)
        {
            _graph = null;
            error = "流程图代码块缺少结束标记。请先修正左侧源码。";
        }
        else
            _graph = LineupDiagram.TryParse(block.Lines, out var graph, out error) ? graph : null;

        if (_graph is { Nodes.Count: > 0 } current)
        {
            var canvas = LineupDiagram.BuildCanvas(current, MakeDiagramIcon, ConfigureNode, ConfigureEdge, CurrentDraggedPositions);
            _canvas = canvas;
            canvas.Background = Brushes.Transparent;
            canvas.RenderTransform = _diagramPan;
            var manual = CurrentDraggedPositions is { Count: > 0 };
            canvas.HorizontalAlignment = manual ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            canvas.VerticalAlignment = manual ? VerticalAlignment.Top : VerticalAlignment.Center;
            ApplyZoom(DiagramZoom.Value);
            DiagramSurface.Content = canvas;
            if (revealNode && canvas.Children.OfType<Border>().FirstOrDefault(node => Equals(node.Tag, _selectedNode)) is { } node)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (ReferenceEquals(canvas, _canvas) && node.IsVisible) node.BringIntoView();
                }, DispatcherPriority.Loaded);
            }
        }
        else
        {
            DiagramSurface.Content = CreateDiagramMessage(_graph is null ? error : null);
            ClearGraphSelection();
        }
        UpdateGraphInspector();
    }

    private static TextBlock CreateDiagramMessage(string? error = null) => new()
    {
        Text = error ?? "从上方拖动干员头像到这里，开始绘制流程图。",
        Foreground = error is null ? Brushes.LightSlateGray : Brushes.Gold,
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(14)
    };

    private void OnDiagramPanStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _canvas is null || Mouse.Captured is not null) return;
        if (!DiagramSurface.CaptureMouse()) return;
        _panStart = e.GetPosition(DiagramSurface);
        _panOffset = new Vector(_diagramPan.X, _diagramPan.Y);
        DiagramSurface.Cursor = Cursors.SizeAll;
        DiagramSurface.ForceCursor = true;
        e.Handled = true;
    }

    private void OnDiagramMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is { } start)
        {
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                var delta = e.GetPosition(DiagramSurface) - start;
                SetDiagramPan(_panOffset + delta);
                e.Handled = true;
                return;
            }
            else EndDiagramPan();
        }
        OnConnectionPointerMove(sender, e);
    }

    private void OnConnectionPointerMove(object sender, MouseEventArgs e)
    {
        if (_connectionPreview is not null) UpdateConnectionPreview(e.GetPosition(DiagramViewport));
    }

    private void OnDiagramLayoutUpdated(object? sender, EventArgs e) => RefreshConnectionPointer();

    private void RefreshConnectionPointer()
    {
        if (_connectionPreview is not null) UpdateConnectionPreview(Mouse.GetPosition(DiagramViewport));
    }

    internal void SetDiagramPan(Vector offset)
    {
        _diagramPan.X = offset.X;
        _diagramPan.Y = offset.Y;
        RefreshConnectionPointer();
    }

    private void OnResetDiagramView(object sender, RoutedEventArgs e) => ResetDiagramView();

    private void ResetDiagramView()
    {
        EndDiagramPan();
        SetDiagramPan(default);
        DiagramSurface.ScrollToHorizontalOffset(0);
        DiagramSurface.ScrollToVerticalOffset(0);
    }

    private void OnDiagramPanEnd(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _panStart is null) return;
        EndDiagramPan();
        e.Handled = true;
    }

    private void OnDiagramPanLostCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DiagramSurface)) EndDiagramPan();
    }

    private void EndDiagramPan()
    {
        if (_panStart is null) return;
        _panStart = null;
        DiagramSurface.ClearValue(CursorProperty);
        DiagramSurface.ClearValue(ForceCursorProperty);
        if (DiagramSurface.IsMouseCaptured) DiagramSurface.ReleaseMouseCapture();
    }

    private FrameworkElement MakeDiagramIcon(bool equipment, string name, int size)
    {
        var path = Vm?.FindIcon(equipment, name);
        if (string.IsNullOrEmpty(path))
            return new TextBlock { Text = "?", Foreground = Brushes.LightSlateGray, ToolTip = name };
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, ToolTip = name };
        DeferredIcon.SetPath(image, path);
        return image;
    }

    private void OnDiagramChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingDiagramPicker || DiagramPicker.SelectedIndex < 0) return;
        _graphIndex = DiagramPicker.SelectedIndex;
        ClearGraphSelection();
        ResetDiagramView();
        RenderDiagram();
    }

    private void OnDiagramZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyZoom(e.NewValue);

    private void ApplyZoom(double scale)
    {
        if (_canvas is not null) _canvas.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private Vector GetCanvasCenteringOffset()
    {
        if (_canvas is null || CurrentDraggedPositions is { Count: > 0 }) return default;
        var origin = _canvas.TranslatePoint(new Point(), DiagramViewport);
        return new Vector(
            Math.Max(0, origin.X + DiagramSurface.HorizontalOffset - _diagramPan.X) / DiagramZoom.Value,
            Math.Max(0, origin.Y + DiagramSurface.VerticalOffset - _diagramPan.Y) / DiagramZoom.Value);
    }

    private Dictionary<string, Point> BeginManualLayout()
    {
        var positions = _graph!.Positions;
        if (positions.Count > 0) return positions;
        var inset = GetCanvasCenteringOffset();
        // Centering is a viewport offset. Carry it into the temporary positions before anchoring the canvas.
        if (_canvas is not null && inset.LengthSquared > 0)
        {
            foreach (var node in _canvas.Children.OfType<Border>())
                if (node.Tag is string id)
                    positions[id] = new Point(Canvas.GetLeft(node), Canvas.GetTop(node)) + inset;
        }
        return positions;
    }

    private static Point ClampDiagramPosition(Point point) => new(
        Math.Clamp(point.X, -MaxDiagramCoordinate, MaxDiagramCoordinate),
        Math.Clamp(point.Y, -MaxDiagramCoordinate, MaxDiagramCoordinate));

    private void PlaceDiagramNode(string id, Point position)
    {
        var positions = BeginManualLayout();
        var shift = new Vector(Math.Max(0, -position.X), Math.Max(0, -position.Y));
        if (shift.LengthSquared > 0)
        {
            // Keep the canvas coordinates nonnegative without moving the graph on screen.
            if (_canvas is not null)
            {
                foreach (var card in _canvas.Children.OfType<Border>())
                    if (card.Tag is string nodeId)
                        positions.TryAdd(nodeId, new Point(Canvas.GetLeft(card), Canvas.GetTop(card)));
            }
            foreach (var key in positions.Keys.ToArray()) positions[key] = PortablePosition(positions[key] + shift);
            SetDiagramPan(new Vector(_diagramPan.X, _diagramPan.Y) - shift * DiagramZoom.Value);
        }
        positions[id] = PortablePosition(position + shift);
    }

    private static Point PortablePosition(Point position) => new(
        Math.Clamp(Math.Round(position.X), 0, LineupDiagram.MaxPosition),
        Math.Clamp(Math.Round(position.Y), 0, LineupDiagram.MaxPosition));

    private void OnAddNode(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || Vm is not { } vm || GetCurrentGraph() is null) return;
        var equipment = Equals(button.Tag, "gear");
        var names = equipment
            ? vm.Members.SelectMany(member => new[] { member.Equipment1, member.Equipment2 })
            : vm.Members.Select(member => member.OperatorName);
        var menu = CreateDiagramMenu();
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        foreach (var name in names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal))
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => AddDiagramNode(equipment, name);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = equipment ? "当前阵容没有已配置的装备" : "当前阵容没有干员", IsEnabled = false });
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private ContextMenu CreateDiagramMenu() => new()
    {
        Style = (Style)FindResource("DiagramContextMenu"),
        Background = (Brush)FindResource("CardBackgroundBrush"),
        BorderBrush = (Brush)FindResource("BorderBrush")
    };

    private void OnDiagramDragOver(object sender, DragEventArgs e)
    {
        var accepted = TryGetDiagramDrop(e, out _, out _);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        if (accepted) DiagramDropZone.BorderBrush = (Brush)FindResource("AccentBrush");
        else ResetDiagramDropHighlight();
        e.Handled = true;
    }

    private void OnDiagramDragLeave(object sender, DragEventArgs e) => ResetDiagramDropHighlight();

    private void OnDiagramDrop(object sender, DragEventArgs e)
    {
        ResetDiagramDropHighlight();
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (!TryGetDiagramDrop(e, out var equipment, out var name)) return;

        var point = _canvas is null
            ? e.GetPosition(DiagramViewport)
            : e.GetPosition(_canvas) + GetCanvasCenteringOffset();
        if (_canvas is null)
            point = new Point(point.X / DiagramZoom.Value, point.Y / DiagramZoom.Value);
        var position = ClampDiagramPosition(point - new Vector(LineupDiagram.CardWidth / 2, LineupDiagram.CardHeight / 2));
        if (AddDiagramNode(equipment, name, position)) e.Effects = DragDropEffects.Copy;
    }

    private bool TryGetDiagramDrop(DragEventArgs e, out bool equipment, out string name)
    {
        equipment = e.Data.GetDataPresent(LineupDragDrop.EquipmentFormat, autoConvert: false);
        var format = equipment ? LineupDragDrop.EquipmentFormat : LineupDragDrop.OperatorFormat;
        name = "";
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Copy) ||
            !e.Data.GetDataPresent(format, autoConvert: false) ||
            e.Data.GetData(format, autoConvert: false) is not string item ||
            string.IsNullOrWhiteSpace(item) || Vm?.FindIcon(equipment, item) is null ||
            GetCurrentGraph() is not { Nodes.Count: < LineupDiagram.MaxNodes }) return false;
        name = item;
        return true;
    }

    private void ResetDiagramDropHighlight()
        => DiagramDropZone.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

    private bool AddDiagramNode(bool equipment, string name, Point? position = null)
    {
        if (GetCurrentGraph() is not { } graph) return false;
        if (graph.Nodes.Count >= LineupDiagram.MaxNodes)
        {
            GraphHint.Text = $"每张流程图最多支持 {LineupDiagram.MaxNodes} 个节点。";
            return false;
        }
        var number = 1;
        while (graph.Nodes.ContainsKey($"N{number}")) number++;
        var id = $"N{number}";
        graph.Nodes.Add(id, new LineupDiagram.Node(id, equipment, name));
        ClearGraphSelection();
        _selectedNode = id;
        return CommitGraph(graph, revealNode: true, placement: position);
    }

    private void OnConnectNodes(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is not { } id) return;
        if (_connectFrom is null) StartNodeConnection(id);
        else
        {
            _connectFrom = null;
            UpdateGraphInspector();
        }
    }

    private void StartNodeConnection(string id)
    {
        if (GetCurrentGraph() is not { } graph || !graph.Nodes.ContainsKey(id)) return;
        _selectedNode = id;
        _selectedEdge = null;
        _connectFrom = id;
        UpdateGraphInspector();
    }

    private void SyncConnectionPreview(Canvas canvas)
    {
        if (_connectFrom is not { } id)
        {
            if (_connectionPreview is { } preview) canvas.Children.Remove(preview);
            _connectionPreview = null;
            return;
        }
        var card = canvas.Children.OfType<Border>().FirstOrDefault(node => Equals(node.Tag, id));
        if (card is null) return;
        if (_connectionPreview is null)
        {
            _connectionPreview = new Line
            {
                Stroke = Brushes.LightSkyBlue, StrokeThickness = 1.5, StrokeDashArray = [4, 3],
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_connectionPreview, -1);
            canvas.Children.Add(_connectionPreview);
        }
        _connectionPreview.X1 = Canvas.GetLeft(card) + LineupDiagram.CardWidth / 2;
        _connectionPreview.Y1 = Canvas.GetTop(card) + LineupDiagram.CardHeight / 2;
        RefreshConnectionPointer();
    }

    internal void UpdateConnectionPreview(Point pointer)
    {
        if (_connectionPreview is not { } line || _canvas is not { } canvas) return;
        // The scroll content presenter clips the segment at the visible canvas boundary.
        var end = DiagramViewport.TranslatePoint(pointer, canvas);
        line.X2 = end.X;
        line.Y2 = end.Y;
    }

    private void SelectNode(string id)
    {
        if (GetCurrentGraph() is not { } graph || !graph.Nodes.ContainsKey(id)) return;
        if (_connectFrom is { } from)
        {
            if (from == id)
            {
                GraphHint.Text = "请选择另一个节点作为连线终点。";
                return;
            }
            _connectFrom = null;
            var existing = graph.Edges.FindIndex(edge => edge.From == from && edge.To == id);
            if (existing >= 0)
            {
                SelectEdge(existing);
                return;
            }
            if (graph.Edges.Count >= LineupDiagram.MaxEdges)
            {
                UpdateGraphInspector();
                GraphHint.Text = $"每张流程图最多支持 {LineupDiagram.MaxEdges} 条连线。";
                return;
            }
            graph.Edges.Add(new LineupDiagram.Edge(from, id, ""));
            _selectedNode = null;
            _selectedEdge = graph.Edges.Count - 1;
            CommitGraph(graph);
            return;
        }
        _selectedNode = id;
        _selectedEdge = null;
        UpdateGraphInspector();
    }

    private void SelectEdge(int index)
    {
        if (GetCurrentGraph() is not { } graph || index >= graph.Edges.Count) return;
        _connectFrom = null;
        _selectedNode = null;
        _selectedEdge = index;
        UpdateGraphInspector();
    }

    private void OnDeleteGraphItem(object sender, RoutedEventArgs e)
        => DeleteGraphItem(_selectedNode, _selectedEdge);

    private void DeleteGraphItem(string? nodeId, int? edgeIndex)
    {
        if (GetCurrentGraph() is not { } graph) return;
        if (nodeId is { } id)
        {
            if (!graph.Nodes.Remove(id)) return;
            graph.Edges.RemoveAll(edge => edge.From == id || edge.To == id);
            CurrentDraggedPositions?.Remove(id);
            graph.Notes.Remove(id);
            graph.Highlights.Remove(id);
        }
        else if (edgeIndex is { } index && index < graph.Edges.Count)
            graph.Edges.RemoveAt(index);
        else return;
        ClearGraphSelection();
        CommitGraph(graph);
    }

    private void OnResetDiagramLayout(object sender, RoutedEventArgs e)
    {
        if (GetCurrentGraph() is not { Positions.Count: > 0 } graph) return;
        graph.Positions.Clear();
        _connectFrom = null;
        ResetDiagramView();
        CommitGraph(graph);
    }

    private void OnApplyGraphProperties(object sender, RoutedEventArgs e) => TryApplyPendingGraphProperties();

    internal bool TryApplyPendingGraphProperties()
    {
        if (DiagramSourceHost.Visibility == Visibility.Visible) return true;
        if (GetCurrentGraph() is not { } graph || (_selectedNode is null && _selectedEdge is null)) return true;
        var text = GraphPropertyText.Text.Trim();
        if (text.Length > (_selectedEdge is null ? 60 : 40))
        {
            GraphHint.Text = "内容过长，请缩短注释或连线文字。";
            return false;
        }
        if (text.Contains('\r') || text.Contains('\n') || (_selectedEdge is not null && text.Contains('|')))
        {
            GraphHint.Text = _selectedEdge is not null ? "连线文字请使用单行，且不要包含 |。" : "注释请使用单行。";
            return false;
        }
        if (_selectedNode is { } id)
        {
            if (graph.Notes.GetValueOrDefault(id, "") == text &&
                graph.Highlights.Contains(id) == (HighlightNode.IsChecked == true)) return true;
            if (text.Length == 0) graph.Notes.Remove(id);
            else graph.Notes[id] = text;
            if (HighlightNode.IsChecked == true) graph.Highlights.Add(id);
            else graph.Highlights.Remove(id);
        }
        else if (_selectedEdge is { } index)
        {
            if (graph.Edges[index].Label == text) return true;
            graph.Edges[index] = graph.Edges[index] with { Label = text };
        }
        _connectFrom = null;
        return CommitGraph(graph);
    }

    private void OnGraphPropertyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnApplyGraphProperties(sender, e);
        e.Handled = true;
    }

    private void ConfigureNode(Border card, LineupDiagram.Node node, Action<Point> moveNode)
    {
        card.Tag = node.Id;
        card.Cursor = Cursors.SizeAll;
        card.Focusable = true;
        System.Windows.Automation.AutomationProperties.SetName(card, node.Name);
        var menu = CreateDiagramMenu();
        var connect = new MenuItem { Header = "从此节点创建连线" };
        var delete = new MenuItem { Header = "删除节点" };
        connect.Click += (_, _) => StartNodeConnection(node.Id);
        delete.Click += (_, _) => DeleteGraphItem(node.Id, null);
        menu.Items.Add(connect);
        menu.Items.Add(delete);
        card.ContextMenu = menu;
        Point? down = null;
        var origin = new Point();
        var startingInset = new Vector();
        var moved = false;
        card.MouseLeftButtonDown += (_, e) =>
        {
            var connecting = _connectFrom is not null;
            var canvas = _canvas;
            var inset = GetCanvasCenteringOffset();
            SelectNode(node.Id);
            if (!connecting && canvas is not null && ReferenceEquals(canvas, _canvas))
            {
                card.Focus();
                if (card.CaptureMouse())
                {
                    down = e.GetPosition(DiagramSurface);
                    origin = new Point(Canvas.GetLeft(card), Canvas.GetTop(card));
                    startingInset = inset;
                    moved = false;
                }
            }
            e.Handled = true;
        };
        card.MouseMove += (_, e) =>
        {
            if (down is not { } start || _canvas is null || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(DiagramSurface) - start;
            if (!moved && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            moved = true;
            var position = ClampDiagramPosition(origin + startingInset + delta / DiagramZoom.Value) - GetCanvasCenteringOffset();
            moveNode(position);
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (down is null) return;
            down = null;
            card.ReleaseMouseCapture();
            if (moved && GetCurrentGraph() is { } graph && graph.Nodes.ContainsKey(node.Id))
            {
                var position = ClampDiagramPosition(new Point(Canvas.GetLeft(card), Canvas.GetTop(card)) + GetCanvasCenteringOffset());
                CommitGraph(graph, revealNode: true, placement: position);
            }
            e.Handled = true;
        };
        card.LostMouseCapture += (_, _) =>
        {
            if (down is null) return;
            down = null;
            moveNode(origin);
        };
        card.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            SelectNode(node.Id);
            e.Handled = true;
        };
    }

    private void ConfigureEdge(UIElement hitTarget, int index)
    {
        if (hitTarget is FrameworkElement element)
        {
            element.Tag = index;
            element.Cursor = Cursors.Hand;
            var menu = CreateDiagramMenu();
            var delete = new MenuItem { Header = "删除连线" };
            delete.Click += (_, _) =>
            {
                if (GetCurrentGraph() is not null && ReferenceEquals(element.Parent, _canvas))
                    DeleteGraphItem(null, index);
            };
            menu.Items.Add(delete);
            element.ContextMenu = menu;
        }
        hitTarget.MouseLeftButtonDown += (_, e) =>
        {
            SelectEdge(index);
            e.Handled = true;
        };
    }

    private void UpdateGraphInspector()
    {
        var node = _selectedNode is { } id ? _graph?.Nodes.GetValueOrDefault(id) : null;
        var edge = _selectedEdge is { } index && _graph is { } graph && index < graph.Edges.Count
            ? graph.Edges[index] : null;
        if (node is null) _selectedNode = null;
        if (edge is null) _selectedEdge = null;
        GraphInspector.Visibility = node is not null || edge is not null ? Visibility.Visible : Visibility.Collapsed;
        HighlightNode.Visibility = node is not null ? Visibility.Visible : Visibility.Collapsed;
        ConnectNodesButton.IsEnabled = node is not null;
        ConnectNodesButton.Content = _connectFrom is null ? "连线" : "取消连线";
        DeleteGraphItemButton.IsEnabled = node is not null || edge is not null;
        ResetLayoutButton.IsEnabled = CurrentDraggedPositions is { Count: > 0 };
        AddOperatorNode.IsEnabled = AddEquipmentNode.IsEnabled = _graph is not null;
        GraphHint.Text = _graph is null ? "修正左侧源码后即可继续编辑。"
            : _connectFrom is not null ? "点击另一个节点作为连线终点。"
            : "中键拖动画布，右键节点或连线可打开菜单。";
        if (node is not null)
        {
            GraphSelectionTitle.Text = $"{node.Name} · 节点注释";
            GraphPropertyText.MaxLength = 60;
            GraphPropertyText.Text = _graph!.Notes.GetValueOrDefault(node.Id, "");
            HighlightNode.IsChecked = _graph.Highlights.Contains(node.Id);
        }
        else if (edge is not null)
        {
            GraphSelectionTitle.Text = $"{_graph!.Nodes[edge.From].Name} → {_graph.Nodes[edge.To].Name} · 连线文字";
            GraphPropertyText.MaxLength = 40;
            GraphPropertyText.Text = edge.Label;
        }
        if (_canvas is null) return;
        foreach (var card in _canvas.Children.OfType<Border>())
        {
            if (card.Tag is not string nodeId) continue;
            var highlighted = _graph!.Highlights.Contains(nodeId);
            card.BorderBrush = nodeId == _selectedNode ? Brushes.DeepSkyBlue : highlighted ? Brushes.Gold : Brushes.SlateGray;
            card.BorderThickness = new Thickness(nodeId == _selectedNode || highlighted ? 2 : 1);
        }
        foreach (var line in _canvas.Children.OfType<Polyline>())
            if (line.Tag is int edgeIndex)
                line.Stroke = edgeIndex == _selectedEdge ? new SolidColorBrush(Color.FromArgb(70, 51, 187, 255)) : Brushes.Transparent;
        SyncConnectionPreview(_canvas);
    }

    private void ClearGraphSelection()
    {
        _selectedNode = null;
        _selectedEdge = null;
        _connectFrom = null;
    }

    private LineupDiagram.Graph? GetCurrentGraph()
    {
        if (!string.Equals(_graphSource, SourceEditor.Text, StringComparison.Ordinal))
            RenderDiagram();
        return _graph;
    }

    private bool CommitGraph(LineupDiagram.Graph graph, bool revealNode = false, Point? placement = null)
    {
        var source = SourceEditor.Text;
        var block = _graphBlocks.ElementAtOrDefault(_graphIndex);
        var newline = source.Contains("\r\n") ? "\r\n" : "\n";
        var start = block?.Start ?? source.Length;
        var end = block?.End ?? start;
        var prefix = block is null && source.Length > 0 ? newline + newline : "";
        if (placement is { } position && _selectedNode is { } id) PlaceDiagramNode(id, position);
        var replacement = prefix + LineupDiagramSource.Format(graph, block?.Kind ?? "lineup", newline);
        if (!FitsDescription(IntroductionEditor.Text, source[..start] + replacement + source[end..]))
        {
            ResetDiagramView();
            RenderDiagram();
            GraphHint.Text = DescriptionLimitMessage;
            return false;
        }

        var caret = SourceEditor.SelectionStart;
        SourceEditor.BeginChange();
        try
        {
            SourceEditor.Select(start, end - start);
            SourceEditor.SelectedText = replacement;
            SourceEditor.CaretIndex = caret < start ? caret
                : caret >= end ? caret + replacement.Length - (end - start) : start;
        }
        finally { SourceEditor.EndChange(); }
        _previewTimer.Stop();
        RenderDiagram(keepSelection: true, revealNode: revealNode);
        return true;
    }
}
