using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ASPAssistant.Mac;

internal sealed class FilterPickerWindow : Window
{
    private readonly Dictionary<string, CheckBox> _checks = [];

    private FilterPickerWindow(string title, IEnumerable<string> options, IReadOnlySet<string> selected)
    {
        Title = title;
        Width = 330;
        Height = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new StackPanel { Margin = new Thickness(14), Spacing = 6 };
        foreach (var value in options.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
        {
            var check = new CheckBox { Content = value, IsChecked = selected.Contains(value) };
            _checks[value] = check;
            list.Children.Add(check);
        }
        var apply = new Button { Content = "应用", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        apply.Click += (_, _) => Close(_checks.Where(pair => pair.Value.IsChecked == true)
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal));
        var clear = new Button { Content = "清空", Margin = new Thickness(10) };
        clear.Click += (_, _) =>
        {
            foreach (var check in _checks.Values) check.IsChecked = false;
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(clear);
        actions.Children.Add(apply);
        Content = new DockPanel
        {
            Children =
            {
                actions,
                new ScrollViewer { Content = list }
            }
        };
        DockPanel.SetDock(actions, Dock.Bottom);
    }

    public static async Task<HashSet<string>?> PickAsync(Window owner, string title,
        IEnumerable<string> options, IReadOnlySet<string> selected) =>
        await new FilterPickerWindow(title, options, selected).ShowDialog<HashSet<string>?>(owner);
}
