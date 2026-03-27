using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.GameState;

public partial class GameState : ObservableObject
{
    [ObservableProperty]
    private int _currentRound;

    [ObservableProperty]
    private int _gold;

    [ObservableProperty]
    private List<(string Name, int Count)> _fieldOperators = [];

    [ObservableProperty]
    private List<(string Name, int Count)> _benchOperators = [];

    [ObservableProperty]
    private List<ShopItem> _shopItems = [];

    [ObservableProperty]
    private Dictionary<string, int> _covenantCounts = new();
}
