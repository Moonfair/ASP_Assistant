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

    [ObservableProperty]
    private int? _selectedTierFilter;

    [ObservableProperty]
    private string _selectedTypeFilter = "";

    public List<int?> AvailableTiers { get; private set; } = [];
    public static List<string> AvailableTypes { get; } = ["", "普通", "转职", "专属"];

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

        _allEquipment = equipment;
        AvailableTiers = [null, .. equipment.Select(e => (int?)e.Tier).Distinct().OrderBy(t => t)];
        OnPropertyChanged(nameof(AvailableTiers));

        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTierFilterChanged(int? value) => ApplyFilters();
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = _allEquipment.AsEnumerable();

        if (SelectedTierFilter.HasValue)
            filtered = filtered.Where(e => e.Tier == SelectedTierFilter.Value);

        if (!string.IsNullOrEmpty(SelectedTypeFilter))
        {
            var t = SelectedTypeFilter switch
            {
                "普通" => EquipmentType.Normal,
                "转职" => EquipmentType.JobChange,
                "专属" => EquipmentType.Exclusive,
                _ => (EquipmentType?)null
            };
            if (t.HasValue)
                filtered = filtered.Where(e => e.Type == t.Value);
        }

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(e =>
                e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredEquipment = new ObservableCollection<Equipment>(filtered);
    }
}
