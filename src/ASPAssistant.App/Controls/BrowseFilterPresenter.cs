using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace ASPAssistant.App.Controls;

/// <summary>Moves the existing filter controls between hosts without rebuilding selections.</summary>
public sealed class BrowseFilterPresenter
{
    private readonly FrameworkElement _topHost;
    private readonly ListBox[] _lists;
    private readonly ToggleButton _toggle;
    private readonly Panel? _actionsTopHost;
    private readonly Panel? _tagsTopHost;
    private readonly Dictionary<Expander, bool> _sideExpansion;
    public FrameworkElement? Actions { get; }
    public FrameworkElement? ActiveTags { get; }
    public FrameworkElement Content { get; }
    internal IEnumerable<Expander> Sections => _sideExpansion.Keys;
    public bool IsSideLayout { get; private set; }
    public bool IsExpanded { get => _toggle.IsChecked == true; set => _toggle.IsChecked = value; }
    public int ActiveCount => _lists.Sum(l => l.SelectedItems.Cast<object>().Count(item => l.Items.IndexOf(item) > 0));
    public event Action? Changed;

    public BrowseFilterPresenter(FrameworkElement owner, FrameworkElement topHost, FrameworkElement content,
        ToggleButton toggle, FrameworkElement? actions, FrameworkElement? activeTags, params ListBox[] lists)
    {
        _topHost = topHost;
        Content = content;
        _toggle = toggle;
        _lists = lists;
        Actions = actions;
        ActiveTags = activeTags;
        _actionsTopHost = actions?.Parent as Panel;
        _tagsTopHost = activeTags?.Parent as Panel;
        actions?.SetBinding(FrameworkElement.DataContextProperty, new Binding("DataContext") { Source = owner });
        activeTags?.SetBinding(FrameworkElement.DataContextProperty, new Binding("DataContext") { Source = owner });
        _sideExpansion = (content as Panel)?.Children.OfType<Expander>().ToDictionary(
            e => e, e => e.Header?.ToString() is not ("词条" or "叠层" or "触发时机")) ?? [];
        foreach (var section in _sideExpansion.Keys) section.Tag = false;
        // Explicit source survives removal from the original visual/logical parent.
        content.SetBinding(FrameworkElement.DataContextProperty, new Binding("DataContext") { Source = owner });
        toggle.Checked += OnToggle;
        toggle.Unchecked += OnToggle;
        foreach (var list in lists) list.SelectionChanged += (_, _) => Changed?.Invoke();
        UpdateVisibility();
    }

    public void SetSideLayout(bool side)
    {
        if (side == IsSideLayout) return;
        IsSideLayout = side;
        foreach (var section in _sideExpansion.Keys) section.Tag = side;
        if (side)
        {
            if (_topHost is Panel panel) panel.Children.Remove(Content);
            else if (_topHost is ContentControl host) host.Content = null;
            if (Actions != null) _actionsTopHost?.Children.Remove(Actions);
            if (ActiveTags != null)
            {
                _tagsTopHost?.Children.Remove(ActiveTags);
                ActiveTags.HorizontalAlignment = HorizontalAlignment.Left;
            }
            foreach (var (section, expanded) in _sideExpansion) section.IsExpanded = expanded;
        }
        else
        {
            if (_topHost is Panel panel) panel.Children.Add(Content);
            else if (_topHost is ContentControl host) host.Content = Content;
            if (Actions != null) _actionsTopHost?.Children.Insert(0, Actions);
            if (ActiveTags != null)
            {
                ActiveTags.HorizontalAlignment = HorizontalAlignment.Right;
                _tagsTopHost?.Children.Add(ActiveTags);
            }
            foreach (var section in _sideExpansion.Keys.ToArray())
            {
                _sideExpansion[section] = section.IsExpanded;
                section.IsExpanded = true;
            }
        }
        _toggle.Visibility = side ? Visibility.Collapsed : Visibility.Visible;
        UpdateVisibility();
        Changed?.Invoke();
    }

    public void Reset()
    {
        foreach (var list in _lists)
        {
            list.SelectedIndex = -1;
            list.SelectedIndex = 0;
        }
        Changed?.Invoke();
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        UpdateVisibility();
        Changed?.Invoke();
    }

    private void UpdateVisibility() => Content.Visibility = IsSideLayout || IsExpanded
        ? Visibility.Visible : Visibility.Collapsed;
}
