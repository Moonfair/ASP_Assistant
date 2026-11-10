using System.Windows;
using System.Windows.Controls;

namespace ASPAssistant.App.Views;

public partial class TrackingView : UserControl
{
    public event Action<string>? RemoveTrackingRequested;

    public TrackingView()
    {
        InitializeComponent();
    }

    private void OnRemoveTracking(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string name)
        {
            RemoveTrackingRequested?.Invoke(name);
        }
    }
}
