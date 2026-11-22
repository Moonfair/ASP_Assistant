using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

public partial class GameStateViewModel : ObservableObject
{
    [ObservableProperty]
    private GarrisonHud _hud;

    [ObservableProperty]
    private GarrisonHud _screenHud;
    private bool _usesWindowsMemory;

    public void SetHudSource(GameTargetKind? kind, bool memoryReadingAllowed, bool targetChanged = true)
    {
        _usesWindowsMemory = memoryReadingAllowed && kind == GameTargetKind.Windows;
        if (targetChanged) ScreenHud = default;
        Hud = _usesWindowsMemory ? default : ScreenHud;
    }

    public void UpdateMemoryHud(GarrisonSnapshot? snapshot)
    {
        if (_usesWindowsMemory)
            Hud = GarrisonHud.FromMemory(snapshot) with { UpgradeCost = ScreenHud.UpgradeCost };
    }

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
        ScreenHud = state.Hud;
        Hud = _usesWindowsMemory ? Hud with { UpgradeCost = ScreenHud.UpgradeCost } : ScreenHud;

        FieldOperatorSummary = new ObservableCollection<string>(
            state.FieldOperators.Select(o => $"{o.Name}×{o.Count}"));

        BenchOperatorSummary = new ObservableCollection<string>(
            state.BenchOperators.Select(o => $"{o.Name}×{o.Count}"));

        ShopItems = new ObservableCollection<ShopItem>(state.ShopItems);

        TrackingAlerts = new ObservableCollection<string>(
            state.ShopItems.Where(s => s.IsTracked).Select(s => $"⚠ {s.Name} 在商店!"));
    }
}
