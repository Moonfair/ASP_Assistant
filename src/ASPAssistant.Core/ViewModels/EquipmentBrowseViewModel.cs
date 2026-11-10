using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class EquipmentBrowseViewModel : ObservableObject
{
    private List<Equipment> _allEquipment = [];

    [ObservableProperty]
    private ObservableCollection<Equipment> _filteredEquipment = [];

    [ObservableProperty]
    private string _searchText = "";

    private readonly HashSet<int> _selectedTierFilters = [];
    private readonly HashSet<EquipmentType> _selectedTypeFilters = [];

    public List<int?> AvailableTiers { get; private set; } = [];
    public static List<string> AvailableTypes { get; } = ["", "普通", "转职"];

    public void LoadEquipment(
        List<Equipment> equipment,
        IEnumerable<string> covenantNames,
        IEnumerable<string> manualJobChangeNames)
    {
        var covenants = covenantNames
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();
        var manualSet = new HashSet<string>(manualJobChangeNames, StringComparer.Ordinal);

        foreach (var e in equipment)
        {
            bool descHasCovenant = covenants.Any(c =>
                e.Normal.EffectDescription.Contains(c, StringComparison.Ordinal) ||
                e.Elite.EffectDescription.Contains(c, StringComparison.Ordinal));

            bool nameHasCovenant = covenants.Any(c =>
                e.Name.Contains(c, StringComparison.Ordinal));

            e.Type = descHasCovenant ? EquipmentType.Exclusive
                   : (nameHasCovenant || manualSet.Contains(e.Name)) ? EquipmentType.JobChange
                   : EquipmentType.Normal;
        }

        LoadEquipment(equipment);
    }

    /// <summary>Use an already-classified catalog without altering its equipment types.</summary>
    public void LoadEquipment(List<Equipment> equipment)
    {
        _allEquipment = equipment;
        AvailableTiers = [null, .. equipment.Select(e => (int?)e.Tier).Distinct().OrderBy(t => t)];
        OnPropertyChanged(nameof(AvailableTiers));

        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    public void SetTierFilters(IEnumerable<int> tiers)
    {
        _selectedTierFilters.Clear();
        _selectedTierFilters.UnionWith(tiers);
        ApplyFilters();
    }

    public void SetTypeFilters(IEnumerable<string> types)
    {
        _selectedTypeFilters.Clear();
        foreach (var type in types)
        {
            if (type == "普通") _selectedTypeFilters.Add(EquipmentType.Normal);
            if (type == "转职") _selectedTypeFilters.Add(EquipmentType.JobChange);
        }
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allEquipment.AsEnumerable();

        if (_selectedTierFilters.Count > 0)
            filtered = filtered.Where(e => _selectedTierFilters.Contains(e.Tier));

        if (_selectedTypeFilters.Count > 0)
            filtered = filtered.Where(e => _selectedTypeFilters.Contains(e.Type));

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(e =>
                e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        var results = filtered.ToList();
        if (!FilteredEquipment.SequenceEqual(results))
            FilteredEquipment = new ObservableCollection<Equipment>(results);
    }
}
