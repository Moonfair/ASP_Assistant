using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedOperators = [];

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedEquipment = [];

    [ObservableProperty]
    private Dictionary<string, int> _covenantCounts = new();

    [ObservableProperty]
    private object? _gameState;

    public IReadOnlyList<TrackingEntry> AllEntries =>
        [.. TrackedOperators, .. TrackedEquipment];
    public event Action? TrackingChanged;

    public void AddTracking(string name, TrackingType type)
    {
        if (TryAddTracking(name, type)) TrackingChanged?.Invoke();
    }

    public int TrackLineup(Lineup lineup)
    {
        var added = 0;
        foreach (var member in lineup.Members)
        {
            if (TryAddTracking(member.OperatorName, TrackingType.Operator)) added++;
            if (TryAddTracking(member.Equipment1, TrackingType.Equipment)) added++;
            if (TryAddTracking(member.Equipment2, TrackingType.Equipment)) added++;
        }
        if (added > 0) TrackingChanged?.Invoke();
        return added;
    }

    private bool TryAddTracking(string name, TrackingType type)
    {
        if (string.IsNullOrWhiteSpace(name) || IsTracked(name))
            return false;

        var entry = new TrackingEntry { Name = name, Type = type };
        if (type == TrackingType.Operator)
            TrackedOperators.Add(entry);
        else
            TrackedEquipment.Add(entry);
        return true;
    }

    public void RemoveTracking(string name)
    {
        var opEntry = TrackedOperators.FirstOrDefault(e => e.Name == name);
        if (opEntry != null)
        {
            TrackedOperators.Remove(opEntry);
            TrackingChanged?.Invoke();
            return;
        }

        var eqEntry = TrackedEquipment.FirstOrDefault(e => e.Name == name);
        if (eqEntry != null)
        {
            TrackedEquipment.Remove(eqEntry);
            TrackingChanged?.Invoke();
        }
    }

    public bool IsTracked(string name)
    {
        return TrackedOperators.Any(e => e.Name == name)
            || TrackedEquipment.Any(e => e.Name == name);
    }

    public void LoadEntries(List<TrackingEntry> entries)
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
        foreach (var entry in entries)
        {
            if (entry.Type == TrackingType.Operator)
                TrackedOperators.Add(entry);
            else
                TrackedEquipment.Add(entry);
        }
        TrackingChanged?.Invoke();
    }
}
