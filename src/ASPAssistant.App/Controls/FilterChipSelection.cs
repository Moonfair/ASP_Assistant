using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ASPAssistant.App.Controls;

/// <summary>Multiselect chips whose first item means “全部”, including null placeholders.</summary>
public static class FilterChipSelection
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(FilterChipSelection),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(FilterChipSelection), new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ListBox list) return;
        if ((bool)e.NewValue)
        {
            list.SelectionMode = SelectionMode.Multiple;
            list.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
            list.SelectionChanged += OnSelectionChanged;
        }
        else
        {
            list.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
            list.SelectionChanged -= OnSelectionChanged;
        }
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(list, source) is not ListBoxItem item ||
            list.ItemContainerGenerator.IndexFromContainer(item) != 0) return;

        // WPF does not select null data items by mouse. Select “全部” by index instead.
        list.SelectedItems.Clear();
        list.SelectedIndex = 0;
        item.Focus();
        e.Handled = true;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var list = (ListBox)sender;
        if (list.Items.Count == 0 || (bool)list.GetValue(IsUpdatingProperty)) return;
        list.SetValue(IsUpdatingProperty, true);
        try
        {
            var all = list.Items[0];
            if (e.AddedItems.Cast<object?>().Any(item => Equals(item, all)))
            {
                foreach (var item in list.SelectedItems.Cast<object?>().Where(item => !Equals(item, all)).ToArray())
                    list.SelectedItems.Remove(item);
            }
            else if (list.SelectedItems.Cast<object?>().Any(item => !Equals(item, all)))
            {
                list.SelectedItems.Remove(all);
            }

            if (list.SelectedItems.Count == 0)
                list.SelectedIndex = 0;
        }
        finally
        {
            list.SetValue(IsUpdatingProperty, false);
        }
    }
}
