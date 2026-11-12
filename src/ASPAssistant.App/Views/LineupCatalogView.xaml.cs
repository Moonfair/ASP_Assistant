using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class LineupCatalogView : UserControl
{
    private Point _dragStart;
    private object? _dragItem;
    private LineupEditorViewModel? Vm => DataContext as LineupEditorViewModel;

    public LineupCatalogView()
    {
        InitializeComponent();
        EquipmentTypes.ItemsSource = EquipmentBrowseViewModel.AvailableTypes;
        SizeChanged += (_, _) => OperatorFilterScroll.MaxHeight = Math.Clamp(ActualHeight - 340, 64, 245);
    }

    private void OnOperatorFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { DataContext: OperatorBrowseViewModel vm } list) return;
        var values = list.SelectedItems.OfType<string>().Where(s => s.Length > 0).ToArray();
        switch ((string)list.Tag)
        {
            case "Tier": vm.SetTierFilters(list.SelectedItems.OfType<int>()); break;
            case "Core": vm.SetCoreCovenantFilters(values); break;
            case "Other": vm.SetOtherCovenantFilters(values); break;
            case "Trait": vm.SetTraitTypeFilters(values); break;
            case "Timing": vm.SetTriggerTimingFilters(values); break;
        }
    }

    private void OnSingleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { DataContext: OperatorBrowseViewModel vm } list) return;
        switch ((string)list.Tag)
        {
            case "Availability": vm.SelectedAvailabilityFilter = (AvailabilityFilter)Math.Max(0, list.SelectedIndex); break;
            case "SelfSelected": vm.SelfSelectedOnly = list.SelectedIndex == 1; break;
            case "Stacking": vm.SelectedStackingFilter = list.SelectedIndex switch { 1 => true, 2 => false, _ => null }; break;
        }
    }

    private void OnRemoveFilter(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OperatorFilterTag tag) return;
        var list = tag.Kind switch
        {
            OperatorFilterKind.Tier => TierFilters, OperatorFilterKind.CoreCovenant => CoreFilters,
            OperatorFilterKind.OtherCovenant => OtherFilters, OperatorFilterKind.Trait => TraitFilters,
            OperatorFilterKind.TriggerTiming => TimingFilters, OperatorFilterKind.Availability => AvailabilityFilters,
            OperatorFilterKind.Stacking => StackingFilters, OperatorFilterKind.SelfSelected => SelfSelectedFilters,
            _ => null
        };
        if (list is null) return;
        if (list.SelectionMode == SelectionMode.Single) list.SelectedIndex = 0;
        else list.SelectedItems.Remove(tag.Value);
    }

    private void OnEquipmentFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { DataContext: EquipmentBrowseViewModel vm } list) return;
        if ((string)list.Tag == "Tier") vm.SetTierFilters(list.SelectedItems.OfType<int>());
        else vm.SetTypeFilters(list.SelectedItems.OfType<string>());
    }

    private void OnAddOperator(object sender, RoutedEventArgs e)
    {
        if (OperatorList.SelectedItem is Operator op) Vm?.AddOperator(op);
        else if (Vm is { } vm) vm.Status = "请先选择一名干员。";
    }
    private void OnEquipSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (vm.SelectedMember is null) { vm.Status = "请先在右侧选择一名干员。"; return; }
        if (EquipmentList.SelectedItem is Equipment equipment) vm.Equip(vm.SelectedMember, equipment.Name);
        else vm.Status = "请先选择一件装备。";
    }

    private void OnOperatorDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(OperatorList, e.OriginalSource as DependencyObject) is ListBoxItem) OnAddOperator(sender, e);
    }
    private void OnEquipmentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EquipmentList, e.OriginalSource as DependencyObject) is ListBoxItem) OnEquipSelected(sender, e);
    }
    private void OnOperatorKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OnAddOperator(sender, e); e.Handled = true; } }
    private void OnEquipmentKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OnEquipSelected(sender, e); e.Handled = true; } }

    private void OnCatalogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && sender is ListBox list)
            ShowCatalogPreview(list.SelectedItem);
    }

    private void ShowCatalogPreview(object? item)
    {
        if (Vm is { } vm && item is Operator or Equipment)
            vm.CatalogPreview = item;
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragItem = ItemsControl.ContainerFromElement((ListBox)sender, e.OriginalSource as DependencyObject) is ListBoxItem item ? item.DataContext : null;
        ShowCatalogPreview(_dragItem);
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _dragItem = null; return; }
        var distance = e.GetPosition(this) - _dragStart;
        if (_dragItem is null || (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance)) return;
        var data = new DataObject();
        string iconPath, name;
        LineupDragKind kind;
        if (_dragItem is Operator op)
        {
            data.SetData(LineupDragDrop.OperatorFormat, op.Name);
            (iconPath, name) = (op.IconPath, op.Name);
            kind = LineupDragKind.Operator;
        }
        else if (_dragItem is Equipment equipment)
        {
            data.SetData(LineupDragDrop.EquipmentFormat, equipment.Name);
            (iconPath, name) = (equipment.IconPath, equipment.Name);
            kind = LineupDragKind.Equipment;
        }
        else return;
        _dragItem = null;
        e.Handled = true;
        using var preview = LineupDragPreview.Start(this, iconPath, name);
        LineupDragDrop.RunWithHints(Window.GetWindow(this) ?? (DependencyObject)this, kind,
            () => DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy));
    }
}
