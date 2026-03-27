using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

public partial class EquipmentBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;

    public EquipmentBrowseView()
    {
        InitializeComponent();
    }

    private void OnEquipmentTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is EquipmentCard card && card.DataContext is Equipment eq)
        {
            TrackingRequested?.Invoke(eq.Name, TrackingType.Equipment);
        }
    }
}
