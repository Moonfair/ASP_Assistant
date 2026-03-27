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

    private readonly HashSet<string> _selectedCovenantFilters = [];

    [ObservableProperty]
    private string? _selectedTraitTypeFilter;

    public List<int?> AvailableTiers { get; private set; } = [];
    public List<string?> AvailableCovenants { get; private set; } = [];
    public List<string?> AvailableTraitTypes { get; private set; } = [];

    public void LoadOperators(List<Operator> operators)
    {
        _allOperators = operators;

        AvailableTiers = [null, .. operators.Select(o => (int?)o.Tier).Distinct().OrderBy(t => t)];
        AvailableCovenants = ["", .. operators
            .SelectMany(o => new[] { o.CoreCovenant }.Concat(o.AdditionalCovenants))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct().Order()];
        AvailableTraitTypes = [null, .. operators
            .SelectMany(o => o.Normal.Traits.Select(t => t.TraitType)
                .Concat(o.Elite.Traits.Select(t => t.TraitType)))
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct().Order()];

        OnPropertyChanged(nameof(AvailableTiers));
        OnPropertyChanged(nameof(AvailableCovenants));
        OnPropertyChanged(nameof(AvailableTraitTypes));

        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTierFilterChanged(int? value) => ApplyFilters();
    partial void OnSelectedTraitTypeFilterChanged(string? value) => ApplyFilters();

    public void SetCovenantFilters(IEnumerable<string> covenants)
    {
        _selectedCovenantFilters.Clear();
        foreach (var c in covenants)
            _selectedCovenantFilters.Add(c);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allOperators.AsEnumerable();

        if (SelectedTierFilter.HasValue)
            filtered = filtered.Where(o => o.Tier == SelectedTierFilter.Value);

        if (_selectedCovenantFilters.Count > 0)
            filtered = filtered.Where(o =>
                _selectedCovenantFilters.All(f =>
                    o.CoreCovenant == f || o.AdditionalCovenants.Contains(f)));

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
