using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class OperatorBrowseViewModel : ObservableObject
{
    private List<Operator> _allOperators = [];

    [ObservableProperty]
    private ObservableCollection<Operator> _filteredOperators = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int? _selectedTierFilter;

    [ObservableProperty]
    private string? _selectedCovenantFilter;

    [ObservableProperty]
    private string? _selectedTraitTypeFilter;

    public void LoadOperators(List<Operator> operators)
    {
        _allOperators = operators;
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTierFilterChanged(int? value) => ApplyFilters();
    partial void OnSelectedCovenantFilterChanged(string? value) => ApplyFilters();
    partial void OnSelectedTraitTypeFilterChanged(string? value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = _allOperators.AsEnumerable();

        if (SelectedTierFilter.HasValue)
            filtered = filtered.Where(o => o.Tier == SelectedTierFilter.Value);

        if (!string.IsNullOrEmpty(SelectedCovenantFilter))
            filtered = filtered.Where(o =>
                o.CoreCovenant == SelectedCovenantFilter ||
                o.AdditionalCovenants.Contains(SelectedCovenantFilter));

        if (!string.IsNullOrEmpty(SelectedTraitTypeFilter))
            filtered = filtered.Where(o =>
                o.Normal.Traits.Any(t => t.TraitType == SelectedTraitTypeFilter) ||
                o.Elite.Traits.Any(t => t.TraitType == SelectedTraitTypeFilter));

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(o =>
                o.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredOperators = new ObservableCollection<Operator>(filtered);
    }
}
