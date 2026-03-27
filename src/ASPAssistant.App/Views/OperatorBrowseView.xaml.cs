using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class OperatorBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;
    public Func<string, bool>? IsTrackedCheck { get; set; }

    public OperatorBrowseView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is OperatorBrowseViewModel)
        {
            // Pre-select "全部" (index 0) so the chip appears highlighted and
            // clicking it later always triggers SelectionChanged.
            TierFilterList.SelectedIndex = 0;
            CovenantFilterList.SelectedIndex = 0;
            TraitFilterList.SelectedIndex = 0;
        }
    }

    private void OnOperatorTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard card && card.DataContext is Operator op)
        {
            card.IsTracked = !card.IsTracked;
            card.IsTrackedCheck = IsTrackedCheck;
            TrackingRequested?.Invoke(op.Name, TrackingType.Operator);
        }
    }

    // Tier and Trait filters: single-select, shared null-item fix handler
    private void OnFilterListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb) return;
        var container = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container == null) return;
        if (lb.ItemContainerGenerator.IndexFromContainer(container) != 0) return;

        // WPF cannot select a null item via mouse — force selection programmatically
        lb.SelectedIndex = 0;
        e.Handled = true;
    }

    private void OnTierFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedTierFilter = TierFilterList.SelectedItem as int?;
    }

    private void OnTraitFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedTraitTypeFilter = TraitFilterList.SelectedItem as string;
    }

    // ── Covenant multi-select ────────────────────────────────────────────────

    // Suppress flag prevents cascading SelectionChanged calls during programmatic
    // selection updates inside OnCovenantPreviewMouseDown.
    private bool _suppressCovenantSelectionChanged = false;

    private void OnCovenantPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var lb = CovenantFilterList;
        var container = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container == null) return;

        int idx = lb.ItemContainerGenerator.IndexFromContainer(container);

        _suppressCovenantSelectionChanged = true;

        if (idx == 0) // "全部" clicked — clear all, select only 全部
        {
            for (int i = 1; i < lb.Items.Count; i++)
            {
                if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c)
                    c.IsSelected = false;
            }
            // Use "" (non-null) sentinel item so WPF can track selection without freezing
            container.IsSelected = true;
        }
        else // specific covenant clicked — toggle it
        {
            container.IsSelected = !container.IsSelected;

            // Deselect 全部
            if (lb.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem allContainer)
                allContainer.IsSelected = false;

            // If nothing remains selected, fall back to 全部
            bool anySelected = false;
            for (int i = 1; i < lb.Items.Count; i++)
            {
                if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c && c.IsSelected)
                {
                    anySelected = true;
                    break;
                }
            }
            if (!anySelected)
            {
                if (lb.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem allContainer2)
                    allContainer2.IsSelected = true;
            }
        }

        _suppressCovenantSelectionChanged = false;
        UpdateCovenantFiltersInVm();
        e.Handled = true;
    }

    private void OnCovenantFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCovenantSelectionChanged) return;
        // Handles initial "全部" selection restored by WPF after LoadOperators
        UpdateCovenantFiltersInVm();
    }

    private void UpdateCovenantFiltersInVm()
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;

        var selected = new List<string>();
        for (int i = 1; i < CovenantFilterList.Items.Count; i++)
        {
            if (CovenantFilterList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c
                && c.IsSelected
                && CovenantFilterList.Items[i] is string s)
            {
                selected.Add(s);
            }
        }
        vm.SetCovenantFilters(selected);
    }
}
