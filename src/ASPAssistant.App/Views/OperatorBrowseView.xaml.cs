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
    public event Action<string>? UntrackingRequested;
    public event Action<string, bool>? BanToggleRequested;
    public Func<string, bool>? IsTrackedCheck { get; set; }

    public Func<string, bool>? IsBannedCheck { get; set; }
    public event Action? ClearBansRequested;
    public event Action? ManualScanRequested;

    // ── Scroll-driven filter collapse state ─────────────────────────────────
    private ScrollViewer? _resultsScrollViewer;
    private double _filterContentHeight;   // natural rendered height of the filter panel
    private double _scrollAnchorOffset;    // VerticalOffset at which the panel was last at full height
    private bool _suppressToggleEvents;    // prevents re-entrant handlers during programmatic toggle
    private bool _restoringScrollAnchor;
    private DispatcherOperation? _pendingScrollRestore;

    public OperatorBrowseView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
        FilterToggle.Checked += OnFilterToggleChecked;
        FilterToggle.Click += OnFilterToggleClick;
        PreviewMouseDown += (_, _) => CancelPendingScrollRestore();
        PreviewMouseWheel += (_, _) => CancelPendingScrollRestore();
        PreviewKeyDown += (_, _) => CancelPendingScrollRestore();
        Unloaded += (_, _) => CancelPendingScrollRestore();
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is OperatorBrowseViewModel)
        {
            // Pre-select "全部" (index 0) so the chip appears highlighted and
            // clicking it later always triggers SelectionChanged.
            AvailabilityFilterList.SelectedIndex = 0;
            TierFilterList.SelectedIndex = 0;
            CoreCovenantFilterList.SelectedIndex = 0;
            OtherCovenantFilterList.SelectedIndex = 0;
            TraitFilterList.SelectedIndex = 0;
            StackingFilterList.SelectedIndex = 0;
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
        if (_restoringScrollAnchor || _pendingScrollRestore != null
            || FilterToggle.IsChecked != true
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void OnOperatorBanToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard card && card.DataContext is Operator op)
            BanToggleRequested?.Invoke(op.Name, card.IsBanned);
    }

    private void OnOperatorTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard card && card.DataContext is Operator op)
        {
            if (card.IsTracked)
                TrackingRequested?.Invoke(op.Name, TrackingType.Operator);
            else
                UntrackingRequested?.Invoke(op.Name);
        }
    }

    public void RefreshTrackingStates()
    {
        if (IsTrackedCheck == null) return;
        foreach (var card in FindVisualChildren<OperatorCard>(OperatorListControl))
        {
            if (card.DataContext is Operator op)
            {
                card.RefreshTrackedState(IsTrackedCheck(op.Name));
            }
        }
    }

    public void RefreshBannedStates()
    {
        if (IsBannedCheck == null) return;
        foreach (var card in FindVisualChildren<OperatorCard>(OperatorListControl))
        {
            if (card.DataContext is Operator op)
                card.RefreshBannedState(IsBannedCheck(op.Name));
        }
    }

    private void OnAvailabilityFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedAvailabilityFilter = AvailabilityFilterList.SelectedIndex switch
        {
            1 => Core.ViewModels.AvailabilityFilter.Available,
            2 => Core.ViewModels.AvailabilityFilter.Unavailable,
            _ => Core.ViewModels.AvailabilityFilter.All
        };
    }

    private void OnStackingFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedStackingFilter = StackingFilterList.SelectedIndex switch
        {
            1 => true,
            2 => false,
            _ => null
        };
    }

    private void OnClearBansClicked(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } owner) return;

        var result = MessageBox.Show(owner, "确定清除本局的全部禁用记录吗？", "清除 ban 位",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            ClearBansRequested?.Invoke();
    }

    private void OnManualScanClicked(object sender, RoutedEventArgs e)
    {
        ManualScanRequested?.Invoke();
    }

    public void UpdateManualScanStatus(int pending)
    {
        ManualScanStatusText.Text = $"{pending} 录入任务执行中";
        ManualScanStatusText.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMultiFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;

        if (sender == TierFilterList)
            vm.SetTierFilters(TierFilterList.SelectedItems.OfType<int>());
        else if (sender == TraitFilterList)
            vm.SetTraitTypeFilters(TraitFilterList.SelectedItems.OfType<string>());
        else if (sender == TriggerTimingFilterList)
            vm.SetTriggerTimingFilters(TriggerTimingFilterList.SelectedItems.OfType<string>());
        else if (sender == CoreCovenantFilterList)
            vm.SetCoreCovenantFilters(CoreCovenantFilterList.SelectedItems.OfType<string>().Where(s => s.Length > 0));
        else if (sender == OtherCovenantFilterList)
            vm.SetOtherCovenantFilters(OtherCovenantFilterList.SelectedItems.OfType<string>().Where(s => s.Length > 0));
    }

    private void OnFilterTagClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: OperatorFilterTag tag } || tag.IsSeparator) return;

        var list = tag.Kind switch
        {
            OperatorFilterKind.Availability => AvailabilityFilterList,
            OperatorFilterKind.Tier => TierFilterList,
            OperatorFilterKind.CoreCovenant => CoreCovenantFilterList,
            OperatorFilterKind.OtherCovenant => OtherCovenantFilterList,
            OperatorFilterKind.Trait => TraitFilterList,
            OperatorFilterKind.Stacking => StackingFilterList,
            OperatorFilterKind.TriggerTiming => TriggerTimingFilterList,
            _ => null
        };
        if (list == null) return;

        CancelPendingScrollRestore();
        HookScrollViewer();
        var anchor = CaptureScrollAnchor();
        _restoringScrollAnchor = true;
        try
        {
            if (list.SelectionMode == SelectionMode.Single)
                list.SelectedIndex = 0;
            else
                list.SelectedItems.Remove(tag.Value);

            UpdateLayout();
            var target = RestoreScrollAnchor(anchor);
            // Programmatic scrolling must not alter the partially collapsed filter panel,
            // including the origin used for the next genuine user scroll.
            RebaseFilterCollapseAnchor();
            if (target != null && anchor != null)
            {
                var source = OperatorListControl.ItemsSource;
                // WPF measures the virtualized cache in the background, refining estimated
                // heights after UpdateLayout. Correct once more after that work settles.
                _pendingScrollRestore = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    _pendingScrollRestore = null;
                    if (!ReferenceEquals(source, OperatorListControl.ItemsSource)) return;
                    _restoringScrollAnchor = true;
                    try { AlignScrollAnchor(anchor, target); }
                    finally
                    {
                        RebaseFilterCollapseAnchor();
                        _restoringScrollAnchor = false;
                    }
                });
            }
        }
        finally
        {
            _restoringScrollAnchor = false;
        }
        e.Handled = true;
    }

    private sealed record ScrollAnchor(Operator[] PreviousItems, int Index, double ScreenCenterY);

    private void CancelPendingScrollRestore()
    {
        if (_pendingScrollRestore != null) RebaseFilterCollapseAnchor();
        _pendingScrollRestore?.Abort();
        _pendingScrollRestore = null;
    }

    private void RebaseFilterCollapseAnchor()
    {
        if (_resultsScrollViewer == null || _filterContentHeight <= 0 || FilterToggle.IsChecked != true) return;
        double visibleHeight = FilterContentRow.Height.IsAbsolute
            ? FilterContentRow.Height.Value : _filterContentHeight;
        // Include offset refinements made by WPF's background cache measurement too.
        // The next user scroll starts from the panel's current degree of collapse.
        _scrollAnchorOffset = _resultsScrollViewer.VerticalOffset - (_filterContentHeight - visibleHeight);
    }

    private ScrollAnchor? CaptureScrollAnchor()
    {
        if (_resultsScrollViewer is not { } scroll || scroll.ViewportHeight <= 0) return null;
        var card = FindVisualChildren<OperatorCard>(OperatorListControl)
            .Where(c => c.TranslatePoint(new Point(0, c.ActualHeight), scroll).Y > 0 &&
                c.TranslatePoint(new Point(), scroll).Y < scroll.ViewportHeight)
            .MinBy(c => Math.Abs(c.TranslatePoint(new Point(0, c.ActualHeight / 2), scroll).Y - scroll.ViewportHeight / 2));
        if (card?.DataContext is not Operator op) return null;
        var items = OperatorListControl.Items.Cast<Operator>().ToArray();
        return new(items, Array.IndexOf(items, op), card.TranslatePoint(new Point(0, card.ActualHeight / 2), this).Y);
    }

    private Operator? RestoreScrollAnchor(ScrollAnchor? anchor)
    {
        if (anchor == null || _resultsScrollViewer == null || OperatorListControl.Items.Count == 0) return null;

        // Removing one of several OR selections can exclude the old center operator.
        // Prefer the nearest surviving old result in that case (next result wins ties).
        var survivors = OperatorListControl.Items.Cast<Operator>().ToHashSet();
        Operator? target = null;
        for (int distance = 0; distance < anchor.PreviousItems.Length && target == null; distance++)
        {
            int next = anchor.Index + distance;
            int previous = anchor.Index - distance;
            if (next < anchor.PreviousItems.Length && survivors.Contains(anchor.PreviousItems[next]))
                target = anchor.PreviousItems[next];
            else if (previous >= 0 && survivors.Contains(anchor.PreviousItems[previous]))
                target = anchor.PreviousItems[previous];
        }
        if (target == null) return null;

        // The target may be outside the realized range after results are rebuilt.
        FindChild<VirtualizingStackPanel>(OperatorListControl)?.BringIndexIntoViewPublic(OperatorListControl.Items.IndexOf(target));
        UpdateLayout();
        AlignScrollAnchor(anchor, target);
        return target;
    }

    private void AlignScrollAnchor(ScrollAnchor anchor, Operator target)
    {
        if (_resultsScrollViewer is not { } scroll) return;
        // Variable-height virtualized cards can refine the extent after each layout.
        // Correct using actual screen coordinates, also accounting for header wrapping.
        for (int pass = 0; pass < 4; pass++)
        {
            var card = FindVisualChildren<OperatorCard>(OperatorListControl)
                .FirstOrDefault(c => ReferenceEquals(c.DataContext, target));
            if (card == null) break;
            double delta = card.TranslatePoint(new Point(0, card.ActualHeight / 2), this).Y - anchor.ScreenCenterY;
            if (Math.Abs(delta) < 0.5) break;
            double offset = Math.Clamp(scroll.VerticalOffset + delta, 0, scroll.ScrollableHeight);
            if (Math.Abs(offset - scroll.VerticalOffset) < 0.5) break;
            scroll.ScrollToVerticalOffset(offset);
            UpdateLayout();
        }
    }
}
