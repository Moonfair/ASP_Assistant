using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class TrackingView : UserControl
{
    public TrackingView()
    {
        InitializeComponent();
    }

    private void OnRemoveTracking(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrackingViewModel tracking && sender is Button { DataContext: TrackingEntry entry })
            tracking.RemoveTracking(entry.Name);
    }

    private void OnClearTracking(object sender, RoutedEventArgs e)
    {
        if (DataContext is TrackingViewModel tracking &&
            MessageBox.Show(Window.GetWindow(this), "确定取消所有干员和装备的追踪吗？", "取消所有",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            tracking.ClearTracking();
        }
    }
}
