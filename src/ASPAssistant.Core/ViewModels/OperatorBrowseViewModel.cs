using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public enum AvailabilityFilter
{
    All,
    Available,
    Unavailable
}

public partial class OperatorBrowseViewModel(OperatorCatalog? catalog = null) : ObservableObject
{
    private List<Operator> _allOperators = [];
    private Func<string, bool>? _isBannedCheck;

    [ObservableProperty]
    private CovenantAvailability? _covenantAvailability;

    [ObservableProperty]
    private ObservableCollection<Operator> _filteredOperators = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _selfSelectedOnly;

    private readonly HashSet<int> _selectedTierFilters = [];

    [ObservableProperty]
    private AvailabilityFilter _selectedAvailabilityFilter = AvailabilityFilter.All;

    private readonly HashSet<string> _selectedCoreCovenantFilters = [];
    private readonly HashSet<string> _selectedOtherCovenantFilters = [];

    private readonly HashSet<string> _selectedTraitTypeFilters = [];

    [ObservableProperty]
    private bool? _selectedStackingFilter;

    private readonly HashSet<string> _selectedTriggerTimingFilters = [];

    public List<int?> AvailableTiers { get; private set; } = [];
    public List<string?> AvailableCoreCovenants { get; private set; } = [];
    public List<string?> AvailableOtherCovenants { get; private set; } = [];
    public List<string?> AvailableTraitTypes { get; private set; } = [];
    public List<string?> AvailableTriggerTimings { get; private set; } = [];

    /// <summary>
    /// Active selections with their dimension/value, plus non-interactive group separators.
    /// </summary>
    public List<OperatorFilterTag> ActiveFilterTags
    {
        get
        {
            var groups = new List<List<OperatorFilterTag>>();
            if (SelfSelectedOnly)
                groups.Add([new(OperatorFilterKind.SelfSelected, true, "自选干员")]);
            if (SelectedAvailabilityFilter != AvailabilityFilter.All)
                groups.Add([new(OperatorFilterKind.Availability, SelectedAvailabilityFilter,
                    SelectedAvailabilityFilter == AvailabilityFilter.Available ? "本局可用" : "本局不可用")]);
            if (_selectedTierFilters.Count > 0)
                groups.Add(_selectedTierFilters.Order().Select(t => new OperatorFilterTag(OperatorFilterKind.Tier, t, TierToRoman(t))).ToList());
            var allCovenants = _selectedCoreCovenantFilters.Select(c => new OperatorFilterTag(OperatorFilterKind.CoreCovenant, c, c))
                .Concat(_selectedOtherCovenantFilters.Select(c => new OperatorFilterTag(OperatorFilterKind.OtherCovenant, c, c)))
                .OrderBy(t => t.Label).ToList();
            if (allCovenants.Count > 0)
                groups.Add(allCovenants);
            if (_selectedTraitTypeFilters.Count > 0)
                groups.Add(_selectedTraitTypeFilters.Order().Select(t => new OperatorFilterTag(OperatorFilterKind.Trait, t, t)).ToList());
            if (SelectedStackingFilter is bool stacking)
                groups.Add([new(OperatorFilterKind.Stacking, stacking, stacking ? "叠层" : "非叠层")]);
            if (_selectedTriggerTimingFilters.Count > 0)
                groups.Add(_selectedTriggerTimingFilters.Order().Select(t => new OperatorFilterTag(OperatorFilterKind.TriggerTiming, t, $"<{t}>")).ToList());

            var result = new List<OperatorFilterTag>();
            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0) result.Add(new(OperatorFilterKind.Separator, null, "|"));
                result.AddRange(groups[i]);
            }
            return result;
        }
    }

    private static readonly string[] _romanNumerals = ["特殊", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];
    private static string TierToRoman(int tier) =>
        tier >= 0 && tier <= 6 ? _romanNumerals[tier] : tier.ToString();

    public void LoadOperators(List<Operator> operators)
    {
        _allOperators = operators;

        AvailableTiers = [null, .. operators.Select(o => (int?)o.Tier)
            .Concat(catalog is null ? [] : new int?[] { 0 }).Distinct().OrderBy(t => t)];

        var coreSet = operators
            .SelectMany(o => o.CoreCovenants)
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

    public void SetBanChecker(Func<string, bool> checker)
    {
        _isBannedCheck = checker;
        ApplyFilters();
    }

    /// <summary>
    /// Called when the ban set changes externally (e.g. new bans detected or bans cleared).
    /// Re-runs the filter so the operator list reflects the updated ban state.
    /// </summary>
    public void NotifyBansChanged() => ApplyFilters();

    partial void OnSearchTextChanged(string value) => _ = ApplyFiltersWithSelfSelectedAsync();
    partial void OnSelfSelectedOnlyChanged(bool value) => _ = ApplyFiltersWithSelfSelectedAsync();
    partial void OnSelectedStackingFilterChanged(bool? value) => ApplyFilters();
    partial void OnSelectedAvailabilityFilterChanged(AvailabilityFilter value) => ApplyFilters();

    public void SetTierFilters(IEnumerable<int> tiers)
    {
        _selectedTierFilters.Clear();
        _selectedTierFilters.UnionWith(tiers);
        ApplyFilters();
    }

    public void SetTraitTypeFilters(IEnumerable<string> traits)
    {
        _selectedTraitTypeFilters.Clear();
        _selectedTraitTypeFilters.UnionWith(traits.Where(t => !string.IsNullOrEmpty(t)));
        ApplyFilters();
    }

    public void SetTriggerTimingFilters(IEnumerable<string> timings)
    {
        _selectedTriggerTimingFilters.Clear();
        _selectedTriggerTimingFilters.UnionWith(timings.Where(t => !string.IsNullOrEmpty(t)));
        ApplyFilters();
    }

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

    private async Task ApplyFiltersWithSelfSelectedAsync()
    {
        ApplyFilters();
        if (catalog is not null && (SelfSelectedOnly || !string.IsNullOrWhiteSpace(SearchText)))
        {
            await catalog.LoadSelfSelectedAsync();
            ApplyFilters();
        }
    }

    private void ApplyFilters()
    {
        var query = SearchText.Trim();
        var filtered = _allOperators.Concat(catalog?.SelfSelected ?? []);
        if (query.Length == 0)
            filtered = filtered.Where(o => o.SelfSelected == SelfSelectedOnly);

        if (SelectedAvailabilityFilter != AvailabilityFilter.All && _isBannedCheck != null)
        {
            filtered = SelectedAvailabilityFilter == AvailabilityFilter.Unavailable
                ? filtered.Where(o => _isBannedCheck(o.Name))
                : filtered.Where(o => !_isBannedCheck(o.Name));
        }

        if (_selectedTierFilters.Count > 0)
            filtered = filtered.Where(o => _selectedTierFilters.Contains(o.Tier));

        // Core and other covenant dimensions are independent (AND between dimensions,
        // OR within each dimension).
        if (_selectedCoreCovenantFilters.Count > 0)
            filtered = filtered.Where(o => o.CoreCovenants.Any(c => _selectedCoreCovenantFilters.Contains(c)));

        if (_selectedOtherCovenantFilters.Count > 0)
            filtered = filtered.Where(o =>
                o.AdditionalCovenants.Any(c => _selectedOtherCovenantFilters.Contains(c)));

        if (_selectedTraitTypeFilters.Count > 0)
            filtered = filtered.Where(o =>
                o.Normal.Traits.Concat(o.Elite.Traits)
                    .Any(t => _selectedTraitTypeFilters.Contains(t.TraitType)));

        if (SelectedStackingFilter is bool stacking)
            filtered = filtered.Where(o => o.Normal.Traits.Concat(o.Elite.Traits)
                .Any(t => t.TraitDescription.Contains("层数", StringComparison.Ordinal)) == stacking);

        if (_selectedTriggerTimingFilters.Count > 0)
        {
            var tags = _selectedTriggerTimingFilters.Select(t => $"<{t}>").ToArray();
            filtered = filtered.Where(o =>
                o.Normal.Traits.Concat(o.Elite.Traits)
                    .Any(t => tags.Any(tag => t.TraitDescription.Contains(tag, StringComparison.Ordinal))));
        }

        if (query.Length > 0)
            filtered = filtered.Where(o =>
                o.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        var results = filtered.OrderBy(o => o.SelfSelected).ThenBy(o => o.Tier).ToList();
        if (!FilteredOperators.SequenceEqual(results))
            FilteredOperators = new ObservableCollection<Operator>(results);
        OnPropertyChanged(nameof(ActiveFilterTags));
    }
}
