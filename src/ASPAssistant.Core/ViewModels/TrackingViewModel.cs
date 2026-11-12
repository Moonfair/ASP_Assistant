using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel : ObservableObject
{
    public const string AllCovenantsOption = "全部";

    private readonly IReadOnlyList<Operator> _operators;
    private readonly ILookup<string, string> _operatorIcons;
    private readonly ILookup<string, string> _equipmentIcons;
    private readonly OperatorCatalog? _catalog;

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedOperators = [];

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedEquipment = [];

    public IReadOnlyList<TrackingEntry> AllEntries =>
        [.. TrackedOperators, .. TrackedEquipment];
    public IReadOnlyList<string> CoreCovenants { get; }
    public IReadOnlyList<string> AdditionalCovenants { get; }
    public event Action? TrackingChanged;

    public TrackingViewModel(IEnumerable<Operator>? operators = null, IEnumerable<Equipment>? equipment = null,
        OperatorCatalog? catalog = null)
    {
        _catalog = catalog;
        _operators = (operators ?? []).ToArray();
        _operatorIcons = _operators.ToLookup(item => item.Name, item => item.IconPath);
        _equipmentIcons = (equipment ?? []).ToLookup(item => item.Name, item => item.IconPath);
        CoreCovenants = _operators.SelectMany(item => item.CoreCovenants)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().Order().Prepend(AllCovenantsOption).ToArray();
        AdditionalCovenants = _operators.SelectMany(item => item.AdditionalCovenants)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().Order().Prepend(AllCovenantsOption).ToArray();
    }

    public void AddTracking(string name, TrackingType type)
    {
        if (TryAddTracking(name, type)) TrackingChanged?.Invoke();
    }

    public (int Matched, int Added) TrackCovenantCombination(string? coreCovenant, string? additionalCovenant)
    {
        coreCovenant = string.IsNullOrWhiteSpace(coreCovenant) ? null : coreCovenant;
        additionalCovenant = string.IsNullOrWhiteSpace(additionalCovenant) ? null : additionalCovenant;
        if (coreCovenant is null && additionalCovenant is null)
            return (0, 0);

        var names = _operators
            .Where(item => !item.StrategyOnly &&
                ((coreCovenant is not null && item.CoreCovenants.Contains(coreCovenant)) ||
                 (additionalCovenant is not null && item.AdditionalCovenants.Contains(additionalCovenant))))
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToArray();

        var added = 0;
        foreach (var name in names)
        {
            if (TryAddTracking(name, TrackingType.Operator)) added++;
        }
        if (added > 0) TrackingChanged?.Invoke();
        return (names.Length, added);
    }

    public int TrackLineup(Lineup lineup)
    {
        var added = 0;
        foreach (var member in lineup.Members)
        {
            if (TryAddTracking(member.OperatorName, TrackingType.Operator)) added++;
            foreach (var name in member.EquipmentSlots)
                if (TryAddTracking(name, TrackingType.Equipment)) added++;
        }
        if (added > 0) TrackingChanged?.Invoke();
        return added;
    }

    private bool TryAddTracking(string name, TrackingType type)
    {
        if (string.IsNullOrWhiteSpace(name) || IsTracked(name))
            return false;
        if (type == TrackingType.Operator && _operators.Any(item => item.StrategyOnly && item.Name == name))
            return false;

        AddEntry(new TrackingEntry { Name = name, Type = type });
        return true;
    }

    private void AddEntry(TrackingEntry entry)
    {
        if (entry.Type == TrackingType.Operator)
        {
            entry.IconPath = _operatorIcons[entry.Name].FirstOrDefault() ?? _catalog?.FindPortrait(entry.Name) ?? "";
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
