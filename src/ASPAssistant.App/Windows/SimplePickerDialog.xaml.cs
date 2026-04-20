using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ASPAssistant.App.Windows;

/// <summary>
/// Simple modal picker dialog: takes a list of (key, display, iconPath) items and
/// returns the chosen key (or null if cleared / cancelled).
/// </summary>
public partial class SimplePickerDialog : Window
{
    /// <summary>
    /// One picker entry. <see cref="RightLabel"/> is shown right-aligned on the row
    /// (e.g. covenant tags for operator picker); leave null to hide.
    /// </summary>
    public sealed record PickerItem(string Key, string Display, string? IconPath, string? RightLabel = null);

    private readonly List<PickerItem> _allItems;
    private readonly ObservableCollection<PickerItem> _filtered = [];
    private bool _suppressAutoCommit = true;

    /// <summary>
    /// "selected" semantics: null means "no change / cancelled"; empty string means "cleared";
    /// non-empty means user picked that key.
    /// </summary>
    public string? Result { get; private set; }

    public SimplePickerDialog(string title, IEnumerable<PickerItem> items, string? currentKey, string? hint = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        Title = title;
        if (!string.IsNullOrWhiteSpace(hint))
            HintText.Text = hint;
        _allItems = [.. items];
        ItemsList.ItemsSource = _filtered;
        ApplyFilter("");

        if (!string.IsNullOrEmpty(currentKey))
        {
            var match = _filtered.FirstOrDefault(i => i.Key == currentKey);
            if (match is not null)
                ItemsList.SelectedItem = match;
        }

        Loaded += (_, _) =>
        {
            // Allow the pre-selection above to settle (it would otherwise trip the
            // SelectionChanged auto-commit and close the dialog instantly).
            _suppressAutoCommit = false;
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        };
    }

    private void ApplyFilter(string text)
    {
        _filtered.Clear();
        if (string.IsNullOrWhiteSpace(text))
        {
            foreach (var item in _allItems)
                _filtered.Add(item);
        }
        else
        {
            foreach (var item in _allItems)
            {
                if (item.Display.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || item.Key.Contains(text, StringComparison.OrdinalIgnoreCase))
                    _filtered.Add(item);
            }
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(SearchBox.Text);

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAutoCommit) return;
        if (ItemsList.SelectedItem is not PickerItem p) return;
        Result = p.Key;
        DialogResult = true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Result = "";
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
