using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel : ObservableObject
{
    private readonly ILookup<string, string> _operatorIcons;
    private readonly ILookup<string, string> _equipmentIcons;

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedOperators = [];

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedEquipment = [];

    public IReadOnlyList<TrackingEntry> AllEntries =>
        [.. TrackedOperators, .. TrackedEquipment];
    public event Action? TrackingChanged;

    public TrackingViewModel(IEnumerable<Operator>? operators = null, IEnumerable<Equipment>? equipment = null)
    {
        _operatorIcons = (operators ?? []).ToLookup(item => item.Name, item => item.IconPath);
        _equipmentIcons = (equipment ?? []).ToLookup(item => item.Name, item => item.IconPath);
    }

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

        AddEntry(new TrackingEntry { Name = name, Type = type });
        return true;
    }

    private void AddEntry(TrackingEntry entry)
    {
        if (entry.Type == TrackingType.Operator)
        {
            entry.IconPath = _operatorIcons[entry.Name].FirstOrDefault() ?? "";
            TrackedOperators.Add(entry);
        }
        else
        {
            entry.IconPath = _equipmentIcons[entry.Name].FirstOrDefault() ?? "";
            TrackedEquipment.Add(entry);
        }
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

    public void ClearTracking()
    {
        if (TrackedOperators.Count == 0 && TrackedEquipment.Count == 0)
            return;

        TrackedOperators.Clear();
        TrackedEquipment.Clear();
        TrackingChanged?.Invoke();
    }

    public void LoadEntries(List<TrackingEntry> entries)
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
        foreach (var entry in entries)
            AddEntry(entry);
        TrackingChanged?.Invoke();
    }
}
