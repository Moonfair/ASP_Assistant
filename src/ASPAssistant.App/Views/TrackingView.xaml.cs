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

    private void OnCovenantSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TrackCombinationButton.IsEnabled = CoreCovenantPicker.SelectedItem is string &&
                                           AdditionalCovenantPicker.SelectedItem is string;
        CombinationStatus.Text = "";
    }

    private void OnTrackCombination(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TrackingViewModel tracking ||
            CoreCovenantPicker.SelectedItem is not string coreCovenant ||
            AdditionalCovenantPicker.SelectedItem is not string additionalCovenant)
            return;

        var (matched, added) = tracking.TrackCovenantCombination(coreCovenant, additionalCovenant);
        CombinationStatus.Text = (matched, added) switch
        {
            (0, _) => "没有符合该组合的干员。",
            (_, 0) => $"{matched} 名匹配干员已全部追踪。",
            _ => $"已新增 {added} 名干员追踪。"
        };
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
