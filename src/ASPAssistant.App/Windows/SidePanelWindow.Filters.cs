using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    private const double ExpandedSidebarWidth = 240;
    private const double FilterRailWidth = 28;
    private BrowseFilterPresenter? _activeFilters;
    private double _requestedSidebarWidth;
    private double _externalSidebarWidth;
    private bool _sidebarOnRight;
    private bool _changingFilterHost;
    public bool UseSideFilters
    {
        get => LayoutToggle.IsChecked == true;
        set => LayoutToggle.IsChecked = value;
    }
    public event Action<bool>? FilterLayoutChanged;

    private void InitializeFilterLayout()
    {
        if (MainTabs.SelectedIndex < 0) MainTabs.SelectedIndex = 0;
        OperatorView.Filters.Changed += RefreshFilterSidebar;
        EquipmentView.Filters.Changed += RefreshFilterSidebar;
        MainTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, MainTabs)) UpdateFilterHost();
        };
        LayoutToggle.Checked += (_, _) => OnFilterLayoutChanged();
        LayoutToggle.Unchecked += (_, _) => OnFilterLayoutChanged();
        UpdateFilterHost();
    }

    private void OnFilterLayoutChanged()
    {
        UpdateFilterHost();
        FilterLayoutChanged?.Invoke(UseSideFilters);
    }

    private void UpdateFilterHost()
    {
        _changingFilterHost = true;
        try
        {
            SideFilterScroll.Content = null;
            SideFilterActions.Content = null;
            SideFilterTags.Content = null;
            _activeFilters?.SetSideLayout(false);
            _activeFilters = UseSideFilters && SettingsPage.Visibility != Visibility.Visible ? MainTabs.SelectedIndex switch
            {
                0 => OperatorView.Filters,
                1 => EquipmentView.Filters,
                _ => null
            } : null;
            if (_activeFilters != null)
            {
                _activeFilters.SetSideLayout(true);
                SideFilterScroll.Content = _activeFilters.Content;
                SideFilterActions.Content = _activeFilters.Actions;
                SideFilterTags.Content = _activeFilters.ActiveTags;
            }
        }
        finally { _changingFilterHost = false; }
        RefreshFilterSidebar();
    }

    private void RefreshFilterSidebar()
    {
        if (_changingFilterHost) return;
        bool expanded = _activeFilters?.IsExpanded == true;
        SideFilterPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        FilterRail.Visibility = _activeFilters != null && !expanded ? Visibility.Visible : Visibility.Collapsed;
        var count = _activeFilters?.ActiveCount ?? 0;
        SideFilterTagScroll.Visibility = count > 0 && _activeFilters?.ActiveTags is not null
            ? Visibility.Visible : Visibility.Collapsed;
        SideFilterCount.Text = count.ToString();
        FilterRailCount.Text = count.ToString();
        ResetSideFiltersButton.IsEnabled = count > 0;
        SetSidebarWidth(_activeFilters == null ? 0 : expanded ? ExpandedSidebarWidth : FilterRailWidth);
    }

    private void SetSidebarWidth(double width)
    {
        if (width == _requestedSidebarWidth) return;
        _requestedSidebarWidth = width;
        var wasProgrammatic = _isProgrammaticChange;
        _isProgrammaticChange = true;
        try { PositionFilterSidebar(); }
        finally { _isProgrammaticChange = wasProgrammatic; }
    }

    private void PositionFilterSidebar()
    {
        var mainWidth = Math.Clamp(Width - _externalSidebarWidth, 280, 400);
        var externalWidth = _requestedSidebarWidth;
        var onRight = _sidebarOnRight;
        var mainLeft = Left + (onRight ? 0 : _externalSidebarWidth);
        var left = mainLeft - (onRight ? 0 : externalWidth);

        if (_trackedGame is { } tracked)
        {
            var game = ToWindowCoordinates(tracked.Bounds);
            var monitor = ToWindowCoordinates(User32.GetMonitorRect(tracked.Bounds.Right - 1, tracked.Bounds.Top));
            if (!_isUserPositioned)
            {
                if (externalWidth > 0)
                    (left, externalWidth, onRight) = DockFilterSidebar(game, monitor, mainWidth, externalWidth);
                else
                    left = tracked.AttachInside ? monitor.Right - mainWidth : game.Right;
            }
            else if (externalWidth > 0 && Top < game.Bottom && Top + Height > game.Top)
            {
                var mainRight = mainLeft + mainWidth;
                if (mainLeft >= game.Right) onRight = true;
                else if (mainRight <= game.Left) onRight = false;
                else externalWidth = 0;

                if (mainLeft >= monitor.Left && mainRight <= monitor.Right)
                {
                    var space = onRight ? monitor.Right - mainRight : mainLeft - monitor.Left;
                    externalWidth = FitSidebarWidth(space, externalWidth);
                }

                left = mainLeft - (onRight ? 0 : externalWidth);
            }
        }

        if (_externalSidebarWidth != externalWidth)
        {
            // Relax the old limits before applying the new width.
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            Width = mainWidth + externalWidth;
            MinWidth = 280 + externalWidth;
            MaxWidth = 400 + externalWidth;
        }
        _externalSidebarWidth = externalWidth;
        _sidebarOnRight = onRight;

        // Inset filters use the rail's column and open over the assistant's content.
        bool inset = _requestedSidebarWidth > externalWidth;
        var columnWidth = inset ? FilterRailWidth : externalWidth;
        SidebarColumn.Width = new GridLength(onRight ? 0 : columnWidth);
        RightSidebarColumn.Width = new GridLength(onRight ? columnWidth : 0);
        int column = onRight ? 2 : 0;
        Grid.SetColumn(FilterRail, column);
        Grid.SetColumn(SideFilterPanel, inset ? 0 : column);
        Grid.SetColumnSpan(SideFilterPanel, inset ? 3 : 1);
        SideFilterPanel.HorizontalAlignment = inset
            ? (onRight ? HorizontalAlignment.Right : HorizontalAlignment.Left)
            : HorizontalAlignment.Stretch;
        SideFilterPanel.Width = inset ? ExpandedSidebarWidth : double.NaN;
        SideFilterPanel.BorderThickness = FilterRail.BorderThickness = onRight
            ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        ExpandSideFiltersArrow.Text = onRight ? "›" : "‹";
        CollapseSideFiltersButton.Content = onRight ? "‹ 收起" : "› 收起";
        if (double.IsFinite(left)) Left = left;
    }

    private static (double Left, double ExternalWidth, bool OnRight) DockFilterSidebar(
        Rect game, Rect monitor, double mainWidth, double sidebarWidth)
    {
        var totalWidth = mainWidth + sidebarWidth;
        var rightSpace = monitor.Right - game.Right;
        var leftSpace = game.Left - monitor.Left;
        if (rightSpace >= totalWidth) return (game.Right, sidebarWidth, true);
        if (leftSpace >= totalWidth) return (game.Left - totalWidth, sidebarWidth, false);
        if (rightSpace >= mainWidth)
            return (game.Right, FitSidebarWidth(rightSpace - mainWidth, sidebarWidth), true);
        if (leftSpace >= mainWidth)
        {
            var externalWidth = FitSidebarWidth(leftSpace - mainWidth, sidebarWidth);
            return (game.Left - mainWidth - externalWidth, externalWidth, false);
        }

        return rightSpace >= leftSpace
            ? (Math.Max(monitor.Left, monitor.Right - mainWidth), 0, true)
            : (monitor.Left, 0, false);
    }

    private static double FitSidebarWidth(double available, double requested) =>
        available >= requested ? requested : available >= FilterRailWidth ? FilterRailWidth : 0;

    private void OnExpandSideFilters(object sender, RoutedEventArgs e)
    {
        if (_activeFilters != null) _activeFilters.IsExpanded = true;
    }
    private void OnCollapseSideFilters(object sender, RoutedEventArgs e)
    {
        if (_activeFilters != null) _activeFilters.IsExpanded = false;
    }
    private void OnResetSideFilters(object sender, RoutedEventArgs e) => _activeFilters?.Reset();
}
