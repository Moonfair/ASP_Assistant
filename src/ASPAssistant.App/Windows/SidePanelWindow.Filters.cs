using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    private const double ExpandedSidebarWidth = 240;
    private BrowseFilterPresenter? _activeFilters;
    private double _sidebarWidth;
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
        SetSidebarWidth(_activeFilters == null ? 0 : expanded ? ExpandedSidebarWidth : 28);
    }

    private void SetSidebarWidth(double width)
    {
        if (Math.Abs(width - _sidebarWidth) < 0.1) return;
        var mainWidth = Math.Clamp(Width - _sidebarWidth, 280, 400);
        var right = Left + Width;
        var wasProgrammatic = _isProgrammaticChange;
        _isProgrammaticChange = true;
        try
        {
            // Relax the old limits first, then apply new limits to the complete window.
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            _sidebarWidth = width;
            SidebarColumn.Width = new GridLength(width);
            Width = mainWidth + width;
            MinWidth = 280 + width;
            MaxWidth = 400 + width;
            var left = right - Width;
            if (_trackedGame is { } tracked)
            {
                var game = ToWindowCoordinates(tracked.Bounds);
                bool coversGame = left < game.Right && left + Width > game.Left &&
                    Top < game.Bottom && Top + Height > game.Top;
                if (!_isUserPositioned || (width == ExpandedSidebarWidth && coversGame))
                    left = GetAttachedLeft(tracked.Bounds, game, tracked.AttachInside);
            }

            if (double.IsFinite(left)) Left = left;
        }
        finally { _isProgrammaticChange = wasProgrammatic; }
    }

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
