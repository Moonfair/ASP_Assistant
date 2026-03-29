using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class OperatorBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;
    public Func<string, bool>? IsTrackedCheck { get; set; }

    // ── Scroll-driven filter collapse state ─────────────────────────────────
    private ScrollViewer? _resultsScrollViewer;
    private double _filterContentHeight;   // natural rendered height of the filter panel
    private double _scrollAnchorOffset;    // VerticalOffset at which the panel was last at full height
    private bool _suppressToggleEvents;    // prevents re-entrant handlers during programmatic toggle

    public OperatorBrowseView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
        FilterToggle.Checked += OnFilterToggleChecked;
        FilterToggle.Click += OnFilterToggleClick;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is OperatorBrowseViewModel)
        {
            // Pre-select "全部" (index 0) so the chip appears highlighted and
            // clicking it later always triggers SelectionChanged.
            TierFilterList.SelectedIndex = 0;
            CoreCovenantFilterList.SelectedIndex = 0;
            OtherCovenantFilterList.SelectedIndex = 0;
            TraitFilterList.SelectedIndex = 0;
            TriggerTimingFilterList.SelectedIndex = 0;

            // Hook the inner ScrollViewer once the visual tree is ready.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, HookScrollViewer);
        }
    }

    // ── Scroll-driven collapse helpers ──────────────────────────────────────

    private void HookScrollViewer()
    {
        if (_resultsScrollViewer != null) return;
        _resultsScrollViewer = FindChild<ScrollViewer>(OperatorListControl);
        if (_resultsScrollViewer != null)
            _resultsScrollViewer.ScrollChanged += OnResultsScrollChanged;
    }

    private void OnResultsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Only respond to genuine user scrolls.
        // ViewportHeightChange != 0: scroll position adjusted because our FilterContentRow
        //   height change resized the viewport — this is a layout side-effect, not user input.
        // ExtentHeightChange != 0: operator list was rebuilt by a filter/search change and
        //   WPF adjusted the offset — also not user input.
        // Responding to either would create a feedback loop (height → viewport → scroll → height …).
        if (FilterToggle.IsChecked != true
            || e.VerticalChange == 0
            || e.ViewportHeightChange != 0
            || e.ExtentHeightChange != 0) return;

        // Capture natural height and anchor on the first scroll event after opening.
        if (_filterContentHeight <= 0)
        {
            if (FilterContentPanel.ActualHeight <= 0) return;
            _filterContentHeight = FilterContentPanel.ActualHeight;
            // Anchor = position BEFORE this event so the first pixel of scroll begins collapsing.
            _scrollAnchorOffset = e.VerticalOffset - e.VerticalChange;
        }

        // Height is proportional to how far we are from the anchor.
        // Scrolling down (delta > 0) collapses; scrolling up (delta < 0) re-opens.
        double delta = e.VerticalOffset - _scrollAnchorOffset;
        double newHeight = Math.Min(_filterContentHeight, Math.Max(0, _filterContentHeight - delta));

        if (newHeight >= _filterContentHeight)
        {
            // Scrolled back to (or above) the anchor — restore Auto so the row is naturally sized.
            if (!FilterContentRow.Height.IsAuto)
                FilterContentRow.Height = GridLength.Auto;
            return;
        }

        FilterContentRow.Height = new GridLength(newHeight);

        if (newHeight <= 0)
        {
            // Fully collapsed by scroll — uncheck toggle.
            // _suppressToggleEvents prevents OnFilterToggleClick from treating this as a
            // user-initiated partial-collapse click.
            _suppressToggleEvents = true;
            FilterToggle.IsChecked = false;
            _suppressToggleEvents = false;
        }
    }

    private void OnFilterToggleChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents) return;

        // Reset to Auto so the panel naturally re-expands to its full content height.
        // _filterContentHeight and anchor will be re-captured on the next scroll event.
        FilterContentRow.Height = GridLength.Auto;
        _filterContentHeight = 0;
        _scrollAnchorOffset = 0;
    }

    private void OnFilterToggleClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents) return;

        // Detect a click that would close the panel while it is partially scroll-collapsed.
        // In that state we want to re-expand to full height instead of closing.
        bool isPartiallyCollapsed =
            _filterContentHeight > 0 &&
            FilterContentRow.Height.IsAbsolute &&
            FilterContentRow.Height.Value > 0 &&
            FilterContentRow.Height.Value < _filterContentHeight;

        if (isPartiallyCollapsed && FilterToggle.IsChecked == false)
        {
            // The click just set IsChecked = false; override that.
            _suppressToggleEvents = true;
            FilterToggle.IsChecked = true;
            FilterContentRow.Height = GridLength.Auto;
            _filterContentHeight = 0;
            // Update anchor to current position so future scrolling collapses from here.
            _scrollAnchorOffset = _resultsScrollViewer?.VerticalOffset ?? 0;
            _suppressToggleEvents = false;
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void OnOperatorTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard card && card.DataContext is Operator op)
        {
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

    private void OnTriggerTimingFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedTriggerTimingFilter = TriggerTimingFilterList.SelectedItem as string;
    }


    // ── Covenant multi-select ────────────────────────────────────────────────

    // Suppress flag prevents cascading SelectionChanged calls during programmatic
    // selection updates inside HandleCovenantListMouseDown.
    private bool _suppressCovenantSelectionChanged = false;

    private void OnCoreCovenantPreviewMouseDown(object sender, MouseButtonEventArgs e)
        => HandleCovenantListMouseDown(CoreCovenantFilterList, e);

    private void OnOtherCovenantPreviewMouseDown(object sender, MouseButtonEventArgs e)
        => HandleCovenantListMouseDown(OtherCovenantFilterList, e);

    private void HandleCovenantListMouseDown(ListBox lb, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container == null) return;

        int idx = lb.ItemContainerGenerator.IndexFromContainer(container);

        _suppressCovenantSelectionChanged = true;

        if (idx == 0) // "全部" clicked — clear all selections in this row
        {
            for (int i = 1; i < lb.Items.Count; i++)
            {
                if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c)
                    c.IsSelected = false;
            }
            container.IsSelected = true;
        }
        else // specific covenant clicked — toggle it
        {
            container.IsSelected = !container.IsSelected;

            // Deselect 全部 in this row
            if (lb.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem allContainer)
                allContainer.IsSelected = false;

            // If nothing remains selected in this row, fall back to 全部
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

        var core = new List<string>();
        CollectSelectedCovenants(CoreCovenantFilterList, core);
        vm.SetCoreCovenantFilters(core);

        var other = new List<string>();
        CollectSelectedCovenants(OtherCovenantFilterList, other);
        vm.SetOtherCovenantFilters(other);
    }

    private static void CollectSelectedCovenants(ListBox lb, List<string> selected)
    {
        for (int i = 1; i < lb.Items.Count; i++)
        {
            if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c
                && c.IsSelected
                && lb.Items[i] is string s)
                selected.Add(s);
        }
    }
}
