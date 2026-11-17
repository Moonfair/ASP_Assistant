using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Mac;

internal sealed class LineupEditorWindow : Window
{
    private readonly MacWorkspace _workspace;
    private readonly Lineup? _existing;
    private readonly List<LineupMember> _members;
    private readonly StackPanel _memberRows = new() { Spacing = 7 };
    private readonly TextBox _name = new() { Watermark = "阵容名称" };
    private readonly TextBox _author = new() { Watermark = "作者名称" };
    private readonly TextBox _description = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130 };
    private readonly TextBox _tags = new() { Watermark = "盟约标签，用逗号分隔" };
    private readonly TextBox _strategies = new() { Watermark = "可用策略，用逗号分隔" };
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };

    private LineupEditorWindow(MacWorkspace workspace, Lineup? existing)
    {
        _workspace = workspace;
        _existing = existing;
        _members = existing?.Members.ToList() ?? [];
        Title = existing == null ? "新建阵容" : "编辑阵容";
        Width = 850;
        Height = 750;
        MinWidth = 680;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _name.Text = existing?.Name ?? "";
        _author.Text = existing?.Author ?? Environment.UserName;
        _description.Text = existing?.Description ?? "";
        _tags.Text = string.Join("、", existing?.CovenantTags ?? []);
        _strategies.Text = string.Join("、", existing?.AvailableStrategies ?? []);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("290,*"), Margin = new Thickness(15) };
        var browser = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var browserTitle = new TextBlock { Text = "添加干员", FontSize = 16, FontWeight = FontWeight.Bold };
        DockPanel.SetDock(browserTitle, Dock.Top);
        browser.Children.Add(browserTitle);
        var search = new TextBox { Watermark = "搜索干员" };
        DockPanel.SetDock(search, Dock.Top);
        browser.Children.Add(search);
        var list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<Operator>((op, _) => new TextBlock
            {
                Text = $"{op.Name} · {op.TierLabel}阶"
            })
        };
        void RefreshSearch()
        {
            list.ItemsSource = workspace.Operators.Where(op => !op.StrategyOnly &&
                op.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase))
                .OrderBy(op => op.Tier).ThenBy(op => op.Name).Take(100).ToArray();
        }
        search.TextChanged += (_, _) => RefreshSearch();
        RefreshSearch();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Operator op || _members.Any(member => member.OperatorName == op.Name)) return;
            if (_members.Count >= Lineup.MaxMembers) { _error.Text = "最多 30 名干员"; return; }
            _members.Add(new LineupMember { OperatorName = op.Name });
            RenderMembers();
            list.SelectedItem = null;
        };
        browser.Children.Add(list);
        root.Children.Add(browser);

        var right = new DockPanel { LastChildFill = true };
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        var footer = new StackPanel { Spacing = 7 };
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(_error);
        var save = new Button { Content = "保存阵容", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 115 };
        save.Click += (_, _) => Save();
        footer.Children.Add(save);
        right.Children.Add(footer);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "阵容信息", FontSize = 16, FontWeight = FontWeight.Bold });
        body.Children.Add(_name);
        body.Children.Add(_author);
        body.Children.Add(new TextBlock { Text = "干员与装备", FontWeight = FontWeight.Bold });
        body.Children.Add(_memberRows);
        body.Children.Add(new TextBlock { Text = "盟约标签" });
        body.Children.Add(_tags);
        body.Children.Add(new TextBlock { Text = "可用策略" });
        body.Children.Add(_strategies);
        body.Children.Add(new TextBlock { Text = "阵容介绍（Markdown）" });
        var insertDiagram = new Button { Content = "插入流程图模板", HorizontalAlignment = HorizontalAlignment.Left };
        insertDiagram.Click += (_, _) =>
        {
            var names = _members.Take(2).Select(member => member.OperatorName).ToArray();
            if (names.Length == 0) { _error.Text = "请先添加干员，再插入流程图。"; return; }
            var first = "A[{{op:" + names[0] + "}}]";
            var second = names.Length > 1 ? "\nB[{{op:" + names[1] + "}}]\nA --> B" : "";
            var diagram = "```lineup\nflowchart TD\n" + first + second + "\n```";
            _description.Text = (_description.Text?.TrimEnd() is { Length: > 0 } prose
                ? prose + "\n\n" : "") + diagram;
        };
        body.Children.Add(insertDiagram);
        body.Children.Add(_description);
        right.Children.Add(new ScrollViewer { Content = body });
        Content = root;
        RenderMembers();
    }

    private void RenderMembers()
    {
        _memberRows.Children.Clear();
        for (int index = 0; index < _members.Count; index++)
        {
            int current = index;
            var member = _members[index];
            var card = new StackPanel { Spacing = 5, Margin = new Thickness(0, 3) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            var portrait = _workspace.Operators.FirstOrDefault(op => op.Name == member.OperatorName);
            title.Children.Add(AssetImages.Create(_workspace.DataDirectory, portrait?.IconPath, 40));
            title.Children.Add(new TextBlock
            {
                Text = member.OperatorName, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.Bold, MinWidth = 100
            });
            var remove = new Button { Content = "移除" };
            remove.Click += (_, _) => { _members.RemoveAt(current); RenderMembers(); };
            title.Children.Add(remove);
            card.Children.Add(title);
            var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var elite = new CheckBox { Content = "精锐", IsChecked = member.IsElite };
            elite.IsCheckedChanged += (_, _) => _members[current] = _members[current] with { IsElite = elite.IsChecked == true };
            toggles.Children.Add(elite);
            var stacking = new CheckBox { Content = "叠层核心", IsChecked = member.IsStackingCore };
            stacking.IsCheckedChanged += (_, _) => _members[current] = _members[current] with { IsStackingCore = stacking.IsChecked == true };
            toggles.Children.Add(stacking);
            var damage = new CheckBox { Content = "输出核心", IsChecked = member.IsDamageCore };
            damage.IsCheckedChanged += (_, _) => _members[current] = _members[current] with { IsDamageCore = damage.IsChecked == true };
            toggles.Children.Add(damage);
            card.Children.Add(toggles);

            int slots = portrait?.EquipmentSlotCount ?? 2;
            for (int slot = 0; slot < slots; slot++)
            {
                int position = slot;
                var equipment = new ComboBox
                {
                    ItemsSource = new[] { "（空）" }.Concat(_workspace.Equipment.Select(item => item.Name)).ToArray(),
                    SelectedItem = member.EquipmentSlots[slot] is { Length: > 0 } chosen ? chosen : "（空）",
                    Width = 260
                };
                equipment.SelectionChanged += (_, _) =>
                {
                    var value = equipment.SelectedItem as string;
                    if (value == "（空）") value = "";
                    var currentMember = _members[current];
                    _members[current] = position switch
                    {
                        0 => currentMember with { Equipment1 = value ?? "" },
                        1 => currentMember with { Equipment2 = value ?? "" },
                        2 => currentMember with { Equipment3 = value },
                        _ => currentMember with { Equipment4 = value }
                    };
                };
                card.Children.Add(equipment);
            }
            _memberRows.Children.Add(new Border
            {
                BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8), Child = card
            });
        }
    }

    private static List<string> SplitTags(string? text) => (text ?? "")
        .Split([',', '，', '、', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.Ordinal).ToList();

    private void Save()
    {
        try
        {
            var lineup = new Lineup
            {
                Id = _existing?.Id ?? Guid.NewGuid(),
                Name = _name.Text?.Trim() ?? "",
                Author = _author.Text?.Trim() ?? "",
                Description = _description.Text ?? "",
                Members = _members.ToList(),
                CovenantTags = SplitTags(_tags.Text),
                AvailableStrategies = SplitTags(_strategies.Text),
                IsImported = _existing?.IsImported ?? false,
                PublishedLink = _existing?.PublishedLink
            };
            LineupShareService.FromCatalog(_workspace.Operators, _workspace.Equipment.Select(item => item.Name),
                _workspace.OperatorCatalog).Validate(lineup);
            Close(lineup);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            _error.Text = ex.Message;
        }
    }

    public static Task<Lineup?> EditAsync(Window owner, MacWorkspace workspace, Lineup? existing) =>
        new LineupEditorWindow(workspace, existing).ShowDialog<Lineup?>(owner);
}
