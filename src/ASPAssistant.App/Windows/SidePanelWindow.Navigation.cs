using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow
{
    private void OnMorePages(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.ContextMenu is null)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = button, Placement = PlacementMode.Bottom,
                MinWidth = 132, Style = (Style)FindResource("PanelPagesMenu")
            };
            foreach (var tab in MainTabs.Items.OfType<TabItem>().Where(t => t.Visibility == Visibility.Collapsed))
            {
                var item = new MenuItem { Header = tab.Header };
                item.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(TabItem.IsSelected))
                    { Source = tab, Mode = BindingMode.OneWay });
                item.Click += (_, _) => MainTabs.SelectedItem = tab;
                menu.Items.Add(item);
            }
            button.ContextMenu = menu;
        }
        button.ContextMenu.IsOpen = true;
    }
}
