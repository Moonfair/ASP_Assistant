using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

public partial class OperatorBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;
    public Func<string, bool>? IsTrackedCheck { get; set; }

    public OperatorBrowseView()
    {
        InitializeComponent();
    }

    private void OnOperatorTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard card && card.DataContext is Operator op)
        {
            card.IsTracked = !card.IsTracked;
            card.IsTrackedCheck = IsTrackedCheck;
            TrackingRequested?.Invoke(op.Name, TrackingType.Operator);
        }
    }
}
