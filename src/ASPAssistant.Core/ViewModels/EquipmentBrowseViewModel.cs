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

    public void LoadEquipment(List<Equipment> equipment)
    {
        _allEquipment = equipment;
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTierFilterChanged(int? value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = _allEquipment.AsEnumerable();

        if (SelectedTierFilter.HasValue)
            filtered = filtered.Where(e => e.Tier == SelectedTierFilter.Value);

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(e =>
                e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredEquipment = new ObservableCollection<Equipment>(filtered);
    }
}
