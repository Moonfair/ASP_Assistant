using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class GameStateViewModel : ObservableObject
{
    [ObservableProperty]
    private int _currentRound;

    [ObservableProperty]
    private int _gold;

    [ObservableProperty]
    private ObservableCollection<string> _fieldOperatorSummary = [];

    [ObservableProperty]
    private ObservableCollection<string> _benchOperatorSummary = [];

    [ObservableProperty]
    private ObservableCollection<ShopItem> _shopItems = [];

    [ObservableProperty]
    private ObservableCollection<string> _trackingAlerts = [];

    public void UpdateFromGameState(Core.GameState.GameState state)
    {
        CurrentRound = state.CurrentRound;
        Gold = state.Gold;

        FieldOperatorSummary = new ObservableCollection<string>(
            state.FieldOperators.Select(o => $"{o.Name}×{o.Count}"));

        BenchOperatorSummary = new ObservableCollection<string>(
            state.BenchOperators.Select(o => $"{o.Name}×{o.Count}"));

        ShopItems = new ObservableCollection<ShopItem>(state.ShopItems);

        TrackingAlerts = new ObservableCollection<string>(
            state.ShopItems.Where(s => s.IsTracked).Select(s => $"⚠ {s.Name} 在商店!"));
    }
}
