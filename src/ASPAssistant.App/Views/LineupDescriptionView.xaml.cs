using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class LineupDescriptionView : UserControl
{
    private bool _loadingSources;
    private readonly DispatcherTimer _previewTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    private LineupEditorViewModel? Vm => DataContext as LineupEditorViewModel;

    public LineupDescriptionView()
    {
        InitializeComponent();
        DiagramSurface.Content = CreateDiagramMessage();
        SourceEditor.MaxLength = Lineup.MaxDescriptionLength;
        IntroductionEditor.MaxLength = Lineup.MaxDescriptionLength;
        DataContextChanged += (_, _) => LoadSources();
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RenderDiagram();
        };
        IsVisibleChanged += (_, _) =>
        {
            _previewTimer.Stop();
            if (IsVisible && DiagramCanvasHost.Visibility == Visibility.Visible) RenderDiagram();
        };
        Unloaded += (_, _) => _previewTimer.Stop();
    }

    private void OnSourceChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDescription();
        _previewTimer.Stop();
        if (Visibility == Visibility.Visible && DiagramCanvasHost?.Visibility == Visibility.Visible)
            _previewTimer.Start();
    }

    private void OnIntroductionChanged(object sender, TextChangedEventArgs e) => UpdateDescription();

    private void LoadSources()
    {
        if (Vm is not { } vm) return;
        var (introduction, diagram) = LineupDiagramSource.SplitDescription(vm.Description);
        _loadingSources = true;
        try
        {
            IntroductionEditor.Text = introduction;
            SourceEditor.Text = diagram;
        }
        finally { _loadingSources = false; }
    }

    private void UpdateDescription()
    {
        if (!_loadingSources && Vm is { } vm && IntroductionEditor is not null && SourceEditor is not null)
            vm.Description = LineupDiagramSource.CombineDescription(IntroductionEditor.Text, SourceEditor.Text);
    }

    private void OnToggleDiagramMode(object sender, RoutedEventArgs e)
    {
        var showSource = DiagramSourceHost.Visibility != Visibility.Visible;
        if (showSource && !TryApplyPendingGraphProperties()) return;
        EndDiagramPan();
        DiagramCanvasHost.Visibility = showSource ? Visibility.Collapsed : Visibility.Visible;
        DiagramSourceHost.Visibility = showSource ? Visibility.Visible : Visibility.Collapsed;
        DiagramModeButton.Content = showSource ? "画布" : "源码";
        _previewTimer.Stop();
        if (showSource) SourceEditor.Focus();
        else RenderDiagram();
    }

    private void OnFormat(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string format }) return;
        if (format is "bullet" or "number")
        {
            var lines = (IntroductionEditor.SelectionLength == 0 ? "列表项" : IntroductionEditor.SelectedText)
                .Replace("\r\n", "\n").Split('\n');
            var list = string.Join("\n", lines.Select((line, index) =>
                (format == "bullet" ? "- " : $"{index + 1}. ") + line));
            InsertBlock(list);
            return;
        }
        var (start, end) = format switch
        {
            "bold" => ("**", "**"),
            "italic" => ("*", "*"),
            "underline" => ("<u>", "</u>"),
            _ => ("==", "==")
        };
        var text = IntroductionEditor.SelectionLength > 0 ? IntroductionEditor.SelectedText : "文字";
        if (text.Contains('\n') || text.Contains('\r'))
        {
            var formatted = string.Join("\n", text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(line => string.IsNullOrWhiteSpace(line) ? line : start + line + end));
            Insert(formatted, 0, formatted.Length);
            return;
        }
        Insert(start + text + end, start.Length, text.Length);
    }

    private void OnInsertIcon(object sender, RoutedEventArgs e)
    {
        var equipment = sender is Button { Tag: "gear" };
        if (GetPickedIcon(equipment) is not { } name)
        {
            EditorHint.Text = equipment ? "请先选择目录里的装备。" : "请先选择目录里的干员。";
            return;
        }
        var token = equipment ? "gear" : "op";
        Insert("{{" + token + ":" + name + "}}");
    }

    private string? GetPickedIcon(bool equipment)
    {
        var name = (equipment ? EquipmentPicker.Text : OperatorPicker.Text).Trim();
        return name.Length > 0 && Vm?.FindIcon(equipment, name) is not null ? name : null;
    }

    private void OnInsertStrategy(object sender, RoutedEventArgs e)
    {
        var name = StrategyPicker.Text.Trim();
        if (string.IsNullOrEmpty(Vm?.FindStrategyIcon(name)))
        {
            EditorHint.Text = "请先选择目录里的策略。";
            return;
        }
        Insert("{{strategy:" + name + "}}");
    }

    internal void RefreshIconPickers()
    {
        if (Vm is not { } vm) return;
        var operators = vm.Members.Select(member => member.OperatorName).ToHashSet(StringComparer.Ordinal);
        var equipment = vm.Members.SelectMany(member => new[] { member.Equipment1, member.Equipment2 }).ToHashSet(StringComparer.Ordinal);
        var operatorText = OperatorPicker.Text;
        var equipmentText = EquipmentPicker.Text;
        var strategyText = StrategyPicker.Text;
        OperatorPicker.ItemsSource = vm.CatalogOperators.OrderBy(item => !operators.Contains(item.Name)).ToArray();
        EquipmentPicker.ItemsSource = vm.EquipmentNames.Where(name => name.Length > 0)
            .OrderBy(name => !equipment.Contains(name)).ToArray();
        OperatorPicker.Text = operatorText;
        EquipmentPicker.Text = equipmentText;
        StrategyPicker.ItemsSource = vm.StrategyOptions.Where(option => option.Strategy.IconPath.Length > 0)
            .OrderBy(option => !option.IsSelected).Select(option => option.Strategy.Name).ToArray();
        StrategyPicker.Text = strategyText;
    }

    private void OnInsertDiagram(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var op = vm.SelectedMember?.OperatorName ?? vm.CatalogOperators.FirstOrDefault()?.Name ?? "干员名";
        var gear = vm.EquipmentNames.FirstOrDefault(name => name.Length != 0) ?? "装备名";
        var existing = LineupDiagramSource.CombineDescription("", SourceEditor.Text);
        var blocks = LineupDiagramSource.FindBlocks(existing, out var hasUnclosedFence);
        if (hasUnclosedFence || blocks.Count >= LineupDiagram.MaxDiagrams)
        {
            EditorHint.Text = hasUnclosedFence ? "请先结束当前流程图代码块。"
                : $"每篇介绍最多支持 {LineupDiagram.MaxDiagrams} 张流程图。";
            return;
        }
        var template = string.Join("\n", "```lineup", "flowchart TD",
            "A[{{op:" + op + "}}]", "B[{{gear:" + gear + "}}]",
            "A -->|装备| B", "highlight A", "note A: 核心干员", "```");
        var next = existing.Length == 0 ? template : existing + "\n\n" + template;
        if (!FitsDescription(IntroductionEditor.Text, next))
        {
            EditorHint.Text = DescriptionLimitMessage;
            return;
        }
        SourceEditor.SelectAll();
        SourceEditor.SelectedText = next;
        _graphIndex = blocks.Count;
        _previewTimer.Stop();
        if (DiagramCanvasHost.Visibility == Visibility.Visible) RenderDiagram();
    }

    private void OnToggleHelp(object sender, RoutedEventArgs e)
        => SyntaxHelp.Visibility = SyntaxHelp.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void InsertBlock(string text)
    {
        var start = IntroductionEditor.SelectionStart;
        var prefix = start > 0 && IntroductionEditor.Text[start - 1] != '\n' ? "\n" : "";
        var end = start + IntroductionEditor.SelectionLength;
        var suffix = end < IntroductionEditor.Text.Length && IntroductionEditor.Text[end] is '\r' or '\n' ? "" : "\n";
        Insert(prefix + text + suffix);
    }

    private void Insert(string text, int? selectionOffset = null, int selectionLength = 0)
    {
        var start = IntroductionEditor.SelectionStart;
        var end = start + IntroductionEditor.SelectionLength;
        var next = IntroductionEditor.Text[..start] + text + IntroductionEditor.Text[end..];
        if (!FitsDescription(next, SourceEditor.Text))
        {
            EditorHint.Text = DescriptionLimitMessage;
            return;
        }
        IntroductionEditor.SelectedText = text;
        IntroductionEditor.Focus();
        IntroductionEditor.Select(start + (selectionOffset ?? text.Length), selectionLength);
        EditorHint.Text = "左边编辑阵容说明。右边独立编辑流程图，可切换画布和源码。";
    }

    private static bool FitsDescription(string introduction, string diagram)
        => LineupDiagramSource.CombineDescription(introduction, diagram).Length <= Lineup.MaxDescriptionLength;

    private static readonly string DescriptionLimitMessage = $"阵容说明和流程图合计最多支持 {Lineup.MaxDescriptionLength} 字。";
}
