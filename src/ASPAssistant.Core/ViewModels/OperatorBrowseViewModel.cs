using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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

    private readonly HashSet<string> _selectedCoreCovenantFilters = [];
    private readonly HashSet<string> _selectedOtherCovenantFilters = [];

    [ObservableProperty]
    private string? _selectedTraitTypeFilter;

    [ObservableProperty]
    private string? _selectedTriggerTimingFilter;

    public List<int?> AvailableTiers { get; private set; } = [];
    public List<string?> AvailableCoreCovenants { get; private set; } = [];
    public List<string?> AvailableOtherCovenants { get; private set; } = [];
    public List<string?> AvailableTraitTypes { get; private set; } = [];
    public List<string?> AvailableTriggerTimings { get; private set; } = [];

    /// <summary>
    /// Individual tag strings for the active-filter summary bar.
    /// Regular tags are the filter values; "|" items are dimension separators.
    /// </summary>
    public List<string> ActiveFilterTags
    {
        get
        {
            var groups = new List<List<string>>();
            if (SelectedTierFilter.HasValue)
                groups.Add([TierToRoman(SelectedTierFilter.Value)]);
            var allCovenants = _selectedCoreCovenantFilters.Concat(_selectedOtherCovenantFilters).Order().ToList();
            if (allCovenants.Count > 0)
                groups.Add(allCovenants);
            if (!string.IsNullOrEmpty(SelectedTraitTypeFilter))
                groups.Add([SelectedTraitTypeFilter]);
            if (!string.IsNullOrEmpty(SelectedTriggerTimingFilter))
                groups.Add([$"<{SelectedTriggerTimingFilter}>"]);

            var result = new List<string>();
            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0) result.Add("|");
                result.AddRange(groups[i]);
            }
            return result;
        }
    }

    private static readonly string[] _romanNumerals = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];
    private static string TierToRoman(int tier) =>
        tier >= 1 && tier <= 6 ? _romanNumerals[tier] : tier.ToString();

    public void LoadOperators(List<Operator> operators)
    {
        _allOperators = operators;

        AvailableTiers = [null, .. operators.Select(o => (int?)o.Tier).Distinct().OrderBy(t => t)];

        var coreSet = operators
            .Select(o => o.CoreCovenant)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet();
        AvailableCoreCovenants = ["", .. coreSet.Order()];
        AvailableOtherCovenants = ["", .. operators
            .SelectMany(o => o.AdditionalCovenants)
            .Where(c => !string.IsNullOrEmpty(c) && !coreSet.Contains(c))
            .Distinct().Order()];

        AvailableTraitTypes = [null, .. operators
            .SelectMany(o => o.Normal.Traits.Select(t => t.TraitType)
                .Concat(o.Elite.Traits.Select(t => t.TraitType)))
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct().Order()];

        var timingRegex = new Regex(@"<([^>]+)>");
        AvailableTriggerTimings = [null, .. operators
            .SelectMany(o => o.Normal.Traits.Select(t => t.TraitDescription)
                .Concat(o.Elite.Traits.Select(t => t.TraitDescription)))
            .SelectMany(desc => timingRegex.Matches(desc).Select(m => m.Groups[1].Value))
            .Distinct().Order()];

        OnPropertyChanged(nameof(AvailableTiers));
        OnPropertyChanged(nameof(AvailableCoreCovenants));
        OnPropertyChanged(nameof(AvailableOtherCovenants));
        OnPropertyChanged(nameof(AvailableTraitTypes));
        OnPropertyChanged(nameof(AvailableTriggerTimings));

        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTierFilterChanged(int? value) => ApplyFilters();
    partial void OnSelectedTraitTypeFilterChanged(string? value) => ApplyFilters();
    partial void OnSelectedTriggerTimingFilterChanged(string? value) => ApplyFilters();

    public void SetCoreCovenantFilters(IEnumerable<string> covenants)
    {
        _selectedCoreCovenantFilters.Clear();
        foreach (var c in covenants)
            _selectedCoreCovenantFilters.Add(c);
        ApplyFilters();
    }

    public void SetOtherCovenantFilters(IEnumerable<string> covenants)
    {
        _selectedOtherCovenantFilters.Clear();
        foreach (var c in covenants)
            _selectedOtherCovenantFilters.Add(c);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allOperators.AsEnumerable();

        if (SelectedTierFilter.HasValue)
            filtered = filtered.Where(o => o.Tier == SelectedTierFilter.Value);

        // Core and other covenant dimensions are independent (AND between dimensions,
        // OR within each dimension).
        if (_selectedCoreCovenantFilters.Count > 0)
            filtered = filtered.Where(o => _selectedCoreCovenantFilters.Contains(o.CoreCovenant));

        if (_selectedOtherCovenantFilters.Count > 0)
            filtered = filtered.Where(o =>
                o.AdditionalCovenants.Any(c => _selectedOtherCovenantFilters.Contains(c)));

        if (!string.IsNullOrEmpty(SelectedTraitTypeFilter))
            filtered = filtered.Where(o =>
                o.Normal.Traits.Any(t => t.TraitType == SelectedTraitTypeFilter) ||
                o.Elite.Traits.Any(t => t.TraitType == SelectedTraitTypeFilter));

        if (!string.IsNullOrEmpty(SelectedTriggerTimingFilter))
        {
            var tag = $"<{SelectedTriggerTimingFilter}>";
            filtered = filtered.Where(o =>
                o.Normal.Traits.Any(t => t.TraitDescription.Contains(tag)) ||
                o.Elite.Traits.Any(t => t.TraitDescription.Contains(tag)));
        }

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(o =>
                o.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredOperators = new ObservableCollection<Operator>(filtered);
        OnPropertyChanged(nameof(ActiveFilterTags));
    }
}
