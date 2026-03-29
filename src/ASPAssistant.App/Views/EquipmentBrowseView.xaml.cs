using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class EquipmentBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;
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
            TrackingRequested?.Invoke(eq.Name, TrackingType.Equipment);
        }
    }
}
