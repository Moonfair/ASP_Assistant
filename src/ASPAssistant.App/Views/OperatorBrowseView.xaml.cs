using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class OperatorBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;
    public Func<string, bool>? IsTrackedCheck { get; set; }

    public OperatorBrowseView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is OperatorBrowseViewModel)
        {
            // Pre-select "全部" (index 0) so the chip appears highlighted and
            // clicking it later always triggers SelectionChanged.
            TierFilterList.SelectedIndex = 0;
            CovenantFilterList.SelectedIndex = 0;
            TraitFilterList.SelectedIndex = 0;
        }
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

    private void OnTierFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedTierFilter = TierFilterList.SelectedItem as int?;
    }

    private void OnCovenantFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedCovenantFilter = CovenantFilterList.SelectedItem as string;
    }

    private void OnTraitFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedTraitTypeFilter = TraitFilterList.SelectedItem as string;
    }
}
