using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class EquipmentBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;
    public event Action<string>? UntrackingRequested;
    public Func<string, bool>? IsTrackedCheck { get; set; }

    public EquipmentBrowseView()
    {
        InitializeComponent();
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        TierFilterList.SelectedIndex = 0;
        TypeFilterList.SelectedIndex = 0;
    }

    private void OnTierFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not EquipmentBrowseViewModel vm) return;
        vm.SelectedTierFilter = TierFilterList.SelectedItem as int?;
    }

    private void OnTypeFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not EquipmentBrowseViewModel vm) return;
        vm.SelectedTypeFilter = TypeFilterList.SelectedItem as string ?? "";
    }

    private void OnEquipmentTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is EquipmentCard card && card.DataContext is Equipment eq)
        {
            if (card.IsTracked)
                TrackingRequested?.Invoke(eq.Name, TrackingType.Equipment);
            else
                UntrackingRequested?.Invoke(eq.Name);
        }
    }

    public void RefreshTrackingStates()
    {
        if (IsTrackedCheck == null) return;
        foreach (var card in FindVisualChildren<EquipmentCard>(EquipmentListControl))
        {
            if (card.DataContext is Equipment eq)
                card.RefreshTrackedState(IsTrackedCheck(eq.Name));
        }
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
}
