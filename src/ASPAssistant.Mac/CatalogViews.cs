using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.Mac;

internal static class CatalogViews
{
    public static Control CreateOperators(MacWorkspace workspace, MainWindow owner)
    {
        var vm = workspace.OperatorBrowse;
        var selectedTiers = new HashSet<string>();
        var selectedCore = new HashSet<string>();
        var selectedOther = new HashSet<string>();
        var selectedTraits = new HashSet<string>();
        var selectedTimings = new HashSet<string>();

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(2) };
        var search = new TextBox { Watermark = "搜索干员" };
        search.TextChanged += (_, _) => vm.SearchText = search.Text ?? "";
        stack.Children.Add(search);
        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        AddFilter(filters, "等阶", vm.AvailableTiers.Where(t => t.HasValue).Select(t => t!.Value.ToString()), selectedTiers,
            values => vm.SetTierFilters(values.Select(int.Parse)));
        AddFilter(filters, "核心盟约", vm.AvailableCoreCovenants.OfType<string>(), selectedCore, vm.SetCoreCovenantFilters);
        AddFilter(filters, "附加盟约", vm.AvailableOtherCovenants.OfType<string>(), selectedOther, vm.SetOtherCovenantFilters);
        AddFilter(filters, "特质", vm.AvailableTraitTypes.OfType<string>(), selectedTraits, vm.SetTraitTypeFilters);
        AddFilter(filters, "触发时机", vm.AvailableTriggerTimings.OfType<string>(), selectedTimings, vm.SetTriggerTimingFilters);
        void AddFilter(WrapPanel panel, string title, IEnumerable<string> options, HashSet<string> selected,
            Action<IEnumerable<string>> apply)
        {
            var button = new Button { Content = title };
            button.Click += async (_, _) =>
            {
                var result = await FilterPickerWindow.PickAsync(owner, title, options, selected);
                if (result == null) return;
                selected.Clear(); selected.UnionWith(result);
                apply(selected);
                button.Content = selected.Count == 0 ? title : $"{title} {selected.Count}";
            };
            panel.Children.Add(button);
        }
        stack.Children.Add(filters);
        var secondary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var availability = new ComboBox { ItemsSource = new[] { "全部", "本局可用", "本局禁用" }, SelectedIndex = 0, Width = 112 };
        availability.SelectionChanged += (_, _) => vm.SelectedAvailabilityFilter = availability.SelectedIndex switch
        {
            1 => AvailabilityFilter.Available, 2 => AvailabilityFilter.Unavailable, _ => AvailabilityFilter.All
        };
        secondary.Children.Add(availability);
        var stacking = new ComboBox { ItemsSource = new[] { "全部叠层", "叠层", "非叠层" }, SelectedIndex = 0, Width = 112 };
        stacking.SelectionChanged += (_, _) => vm.SelectedStackingFilter = stacking.SelectedIndex switch
        {
            1 => true, 2 => false, _ => null
        };
        secondary.Children.Add(stacking);
        var selfSelected = new CheckBox { Content = "自选干员", VerticalAlignment = VerticalAlignment.Center };
        selfSelected.IsCheckedChanged += (_, _) => vm.SelfSelectedOnly = selfSelected.IsChecked == true;
        secondary.Children.Add(selfSelected);
        stack.Children.Add(secondary);

        var count = new TextBlock { Foreground = MacPalette.Secondary, FontSize = 11 };
        stack.Children.Add(count);
        var list = new ListBox
        {
            ItemsSource = vm.FilteredOperators,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Operator>((op, _) => OperatorRow(op, workspace, owner))
        };
        MacListStyling.Apply(list);
        void Refresh()
        {
            list.ItemsSource = vm.FilteredOperators;
            count.Text = $"{vm.FilteredOperators.Count} 名干员";
        }
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.FilteredOperators)) Dispatcher.UIThread.Post(Refresh);
        };
        workspace.Bans.BansChanged += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
        return new DockPanel
        {
            Children = { Top(stack), list }
        };
    }

    public static Control CreateEquipment(MacWorkspace workspace, MainWindow owner)
    {
        var vm = workspace.EquipmentBrowse;
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(2) };
        var search = new TextBox { Watermark = "搜索装备" };
        search.TextChanged += (_, _) => vm.SearchText = search.Text ?? "";
        stack.Children.Add(search);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var tiers = new HashSet<string>();
        var tierButton = new Button { Content = "等阶" };
        tierButton.Click += async (_, _) =>
        {
            var result = await FilterPickerWindow.PickAsync(owner, "装备等阶",
                vm.AvailableTiers.Where(t => t.HasValue).Select(t => t!.Value.ToString()), tiers);
            if (result == null) return;
            tiers.Clear(); tiers.UnionWith(result);
            vm.SetTierFilters(tiers.Select(int.Parse));
            tierButton.Content = tiers.Count == 0 ? "等阶" : $"等阶 {tiers.Count}";
        };
        filters.Children.Add(tierButton);
        var types = new HashSet<string>();
        var typeButton = new Button { Content = "类型" };
        typeButton.Click += async (_, _) =>
        {
            var result = await FilterPickerWindow.PickAsync(owner, "装备类型", new[] { "普通", "转职" }, types);
            if (result == null) return;
            types.Clear(); types.UnionWith(result);
            vm.SetTypeFilters(types);
            typeButton.Content = types.Count == 0 ? "类型" : $"类型 {types.Count}";
        };
        filters.Children.Add(typeButton);
        stack.Children.Add(filters);
        var count = new TextBlock { Foreground = MacPalette.Secondary, FontSize = 11 };
        stack.Children.Add(count);
        var list = new ListBox
        {
            ItemsSource = vm.FilteredEquipment,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Equipment>((item, _) => EquipmentRow(item, workspace, owner))
        };
        MacListStyling.Apply(list);
        void Refresh()
        {
            list.ItemsSource = vm.FilteredEquipment;
            count.Text = $"{vm.FilteredEquipment.Count} 件装备";
        }
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.FilteredEquipment)) Dispatcher.UIThread.Post(Refresh);
        };
        Refresh();
        return new DockPanel { Children = { Top(stack), list } };
    }

    private static Control Top(Control control)
    {
        DockPanel.SetDock(control, Dock.Top);
        return control;
    }

    private static Control OperatorRow(Operator op, MacWorkspace workspace, MainWindow owner)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("42,*,Auto") };
        row.Children.Add(AssetImages.Create(workspace.DataDirectory, op.IconPath, 38));
        var text = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0) };
        text.Children.Add(new TextBlock { Text = $"{op.Name}  ·  {op.TierLabel}阶", FontSize = 13,
            FontWeight = FontWeight.SemiBold, Foreground = MacPalette.Text });
        text.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", op.CoreCovenants.Concat(op.AdditionalCovenants)),
            Foreground = MacPalette.Secondary, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            MaxHeight = 30
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center };
        var detail = new Button { Content = "详情", FontSize = 11, Padding = new Thickness(8, 4) };
        detail.Click += async (_, _) => await CatalogDetailWindow.ShowOperatorAsync(owner, op, workspace);
        actions.Children.Add(detail);
        var more = new Button { Content = "···", FontSize = 12, Padding = new Thickness(7, 4) };
        var menu = new MenuFlyout();
        var track = new MenuItem { Header = workspace.Tracking.IsTracked(op.Name) ? "取消追踪" : "追踪" };
        track.Click += (_, _) =>
        {
            if (workspace.Tracking.IsTracked(op.Name)) workspace.Tracking.RemoveTracking(op.Name);
            else workspace.Tracking.AddTracking(op.Name, TrackingType.Operator);
            track.Header = workspace.Tracking.IsTracked(op.Name) ? "取消追踪" : "追踪";
        };
        menu.Items.Add(track);
        var ban = new MenuItem { Header = workspace.Bans.IsBanned(op.Name) ? "解除禁用" : "禁用" };
        ban.Click += (_, _) =>
        {
            workspace.Bans.ToggleBan(op.Name);
            workspace.SetManualBan(op.Name, workspace.Bans.IsBanned(op.Name));
            ban.Header = workspace.Bans.IsBanned(op.Name) ? "解除禁用" : "禁用";
        };
        menu.Items.Add(ban);
        more.Flyout = menu;
        actions.Children.Add(more);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return Card(row);
    }

    private static Control EquipmentRow(Equipment item, MacWorkspace workspace, MainWindow owner)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("42,*,Auto") };
        row.Children.Add(AssetImages.Create(workspace.DataDirectory, item.IconPath, 38));
        var text = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0) };
        text.Children.Add(new TextBlock { Text = $"{item.Name}  ·  {item.Tier}阶", FontSize = 13,
            FontWeight = FontWeight.SemiBold, Foreground = MacPalette.Text });
        text.Children.Add(new TextBlock
        {
            Text = item.Normal.EffectDescription, Foreground = MacPalette.Secondary,
            FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 30
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center };
        var track = new Button { Content = workspace.Tracking.IsTracked(item.Name) ? "已追踪" : "追踪",
            FontSize = 11, Padding = new Thickness(8, 4) };
        track.Click += (_, _) =>
        {
            if (workspace.Tracking.IsTracked(item.Name)) workspace.Tracking.RemoveTracking(item.Name);
            else workspace.Tracking.AddTracking(item.Name, TrackingType.Equipment);
            track.Content = workspace.Tracking.IsTracked(item.Name) ? "已追踪" : "追踪";
        };
        actions.Children.Add(track);
        var detail = new Button { Content = "详情", FontSize = 11, Padding = new Thickness(8, 4) };
        detail.Click += async (_, _) => await CatalogDetailWindow.ShowEquipmentAsync(owner, item, workspace);
        actions.Children.Add(detail);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return Card(row);
    }

    private static Control Card(Control content) => new Border
    {
        Background = MacPalette.Surface, BorderBrush = MacPalette.Border,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
        Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 4), Child = content
    };
}
