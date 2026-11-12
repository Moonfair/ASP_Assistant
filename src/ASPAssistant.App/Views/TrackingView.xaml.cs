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
        TrackCombinationButton.IsEnabled = GetSelectedCovenant(CoreCovenantPicker) is not null ||
                                           GetSelectedCovenant(AdditionalCovenantPicker) is not null;
        CombinationStatus.Text = "";
    }

    private static string? GetSelectedCovenant(ComboBox picker) =>
        picker.SelectedItem is string covenant && covenant != TrackingViewModel.AllCovenantsOption ? covenant : null;

    private void OnTrackCombination(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TrackingViewModel tracking) return;

        var (matched, added) = tracking.TrackCovenantCombination(
            GetSelectedCovenant(CoreCovenantPicker), GetSelectedCovenant(AdditionalCovenantPicker));
        CombinationStatus.Text = (matched, added) switch
        {
            (0, _) => "没有符合所选盟约的干员。",
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
