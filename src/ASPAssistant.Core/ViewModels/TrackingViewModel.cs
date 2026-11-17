using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel : ObservableObject
{
    public const string AllCovenantsOption = "全部";

    /// <summary>Marker for entries the user added one by one from the browse tabs.</summary>
    public const string ManualOrigin = "manual";

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

    private enum TrackResult { None, OriginAdded, EntryAdded }

    private static string LineupOrigin(Guid id) => $"lineup:{id:N}";

    private static string CovenantOrigin(string? coreCovenant, string? additionalCovenant) =>
        $"covenant:{coreCovenant}|{additionalCovenant}";

    public void AddTracking(string name, TrackingType type)
    {
        if (TryTrack(name, type, ManualOrigin) != TrackResult.None) TrackingChanged?.Invoke();
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

        var origin = CovenantOrigin(coreCovenant, additionalCovenant);
        var added = 0;
        var changed = false;
        foreach (var name in names)
        {
            switch (TryTrack(name, TrackingType.Operator, origin))
            {
                case TrackResult.EntryAdded:
                    added++;
                    changed = true;
                    break;
                case TrackResult.OriginAdded:
                    changed = true;
                    break;
            }
        }
        if (changed) TrackingChanged?.Invoke();
        return (names.Length, added);
    }

    public int TrackLineup(Lineup lineup)
    {
        var origin = LineupOrigin(lineup.Id);
        var added = 0;
        var changed = false;
        foreach (var (name, type) in EnumerateLineupEntries(lineup))
        {
            switch (TryTrack(name, type, origin))
            {
                case TrackResult.EntryAdded:
                    added++;
                    changed = true;
                    break;
                case TrackResult.OriginAdded:
                    changed = true;
                    break;
            }
        }
        if (changed) TrackingChanged?.Invoke();
        return added;
    }

    /// <summary>
    /// Drops everything the given lineup claimed. Entries still claimed by another lineup,
    /// a covenant combination or a manual toggle stay on the list.
    /// </summary>
    public int UntrackLineup(Lineup lineup)
    {
        var origin = LineupOrigin(lineup.Id);
        var removed = 0;
        foreach (var (name, type) in EnumerateLineupEntries(lineup))
        {
            if (ReleaseOrigin(name, type, origin)) removed++;
        }
        if (removed > 0) TrackingChanged?.Invoke();
        return removed;
    }

    public bool IsLineupTracked(Lineup lineup)
    {
        var trackableOperators = lineup.Members.Select(member => member.OperatorName).Where(CanTrackOperator).ToArray();
        return trackableOperators.Length > 0 &&
            trackableOperators.All(name => TrackedOperators.Any(entry => entry.Name == name));
    }

    private bool CanTrackOperator(string name) =>
        !string.IsNullOrWhiteSpace(name) && !_operators.Any(item => item.StrategyOnly && item.Name == name);

    private static IEnumerable<(string Name, TrackingType Type)> EnumerateLineupEntries(Lineup lineup)
    {
        var entries = new List<(string Name, TrackingType Type)>();
        foreach (var member in lineup.Members)
        {
            if (!string.IsNullOrWhiteSpace(member.OperatorName))
                entries.Add((member.OperatorName, TrackingType.Operator));
            foreach (var slot in member.EquipmentSlots)
                if (!string.IsNullOrWhiteSpace(slot))
                    entries.Add((slot, TrackingType.Equipment));
        }
        return entries.Distinct();
    }

    private TrackingEntry? FindEntry(string name, TrackingType type) => type == TrackingType.Operator
        ? TrackedOperators.FirstOrDefault(entry => entry.Name == name)
        : TrackedEquipment.FirstOrDefault(entry => entry.Name == name);

    private TrackResult TryTrack(string name, TrackingType type, string origin)
    {
        if (string.IsNullOrWhiteSpace(name))
            return TrackResult.None;
        if (type == TrackingType.Operator && !CanTrackOperator(name))
            return TrackResult.None;

        var existing = FindEntry(name, type);
        if (existing is not null)
            return existing.Origins.Add(origin) ? TrackResult.OriginAdded : TrackResult.None;

        var entry = new TrackingEntry { Name = name, Type = type };
        entry.Origins.Add(origin);
        AddEntry(entry);
        return TrackResult.EntryAdded;
    }

    private bool ReleaseOrigin(string name, TrackingType type, string origin)
    {
        var entry = FindEntry(name, type);
        if (entry is null) return false;

        entry.Origins.Remove(origin);
        // A manual toggle never keeps a group member alive on its own, so a lineup tracked
        // before origins were recorded still clears in a single click.
        if (entry.Origins.Any(other => other != ManualOrigin)) return false;

        if (type == TrackingType.Operator) TrackedOperators.Remove(entry);
        else TrackedEquipment.Remove(entry);
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
