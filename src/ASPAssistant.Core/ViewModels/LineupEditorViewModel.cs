using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

public partial class LineupMemberViewModel : ObservableObject
{
    public string OperatorName { get; }
    public Operator? Operator { get; }
    public string IconPath => Operator?.IconPath ?? "";
    public IReadOnlyList<LineupEquipmentSlotViewModel> EquipmentSlots { get; }
    public string Equipment1 { get => EquipmentSlots[0].Name; set => EquipmentSlots[0].Name = value; }
    public string Equipment2 { get => EquipmentSlots[1].Name; set => EquipmentSlots[1].Name = value; }
    public string Equipment1IconPath => EquipmentSlots[0].IconPath;
    public string Equipment2IconPath => EquipmentSlots[1].IconPath;
    public bool CanBecomeElite => Operator is { Tier: > 0 };
    [ObservableProperty] private bool _isStackingCore;
    [ObservableProperty] private bool _isDamageCore;
    [ObservableProperty] private bool _isElite;

    public LineupMemberViewModel(LineupMember member, Operator? op = null, IReadOnlyDictionary<string, Equipment>? equipment = null)
    {
        Operator = op;
        OperatorName = member.OperatorName;
        _isStackingCore = member.IsStackingCore;
        _isDamageCore = member.IsDamageCore;
        _isElite = member.IsElite;
        var slots = member.EquipmentSlots;
        EquipmentSlots = slots.Take(op?.EquipmentSlotCount == 4 ? 4 : 2)
            .Select((name, index) => new LineupEquipmentSlotViewModel(this, index + 1, name, equipment)).ToArray();
        foreach (var slot in EquipmentSlots)
            slot.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LineupEquipmentSlotViewModel.Name))
                    OnPropertyChanged(nameof(EquipmentSlots));
            };
    }

    public LineupMember Snapshot() => new()
    {
        OperatorName = OperatorName, IsStackingCore = IsStackingCore, IsDamageCore = IsDamageCore,
        IsElite = IsElite,
        Equipment1 = Equipment1 ?? "", Equipment2 = Equipment2 ?? "",
        Equipment3 = ExtraEquipment(3), Equipment4 = ExtraEquipment(4)
    };

    private string? ExtraEquipment(int number) =>
        EquipmentSlots.ElementAtOrDefault(number - 1) is { HasEquipment: true } slot ? slot.Name : null;
}

public sealed partial class LineupEquipmentSlotViewModel(LineupMemberViewModel member, int number, string name,
    IReadOnlyDictionary<string, Equipment>? equipment) : ObservableObject
{
    public LineupMemberViewModel Member { get; } = member;
    public int Number { get; } = number;
    public string Tag => Number.ToString();
    public string Label => $"装备 {Number}";
    public string IconPath => equipment?.GetValueOrDefault(Name ?? "")?.IconPath ?? "";
    public bool HasEquipment => !string.IsNullOrEmpty(Name);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IconPath)), NotifyPropertyChangedFor(nameof(HasEquipment))]
    private string _name = name;
}

public sealed partial class LineupStrategyOption(Strategy strategy, bool isSelected) : ObservableObject
{
    public Strategy Strategy { get; } = strategy;
    [ObservableProperty] private bool _isSelected = isSelected;
}

public sealed partial class LineupCovenantOption(string name, bool isSelected) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private bool _isSelected = isSelected;
}

public partial class LineupEditorViewModel : ObservableObject
{
    private readonly List<Operator> _operators;
    private readonly LineupShareService _share;
    private readonly Guid _id;
    private readonly bool _isImported;
    private readonly string? _publishedLink;
    private readonly Dictionary<string, Equipment> _equipment;
    public OperatorBrowseViewModel OperatorBrowser { get; } = new();
    public EquipmentBrowseViewModel EquipmentBrowser { get; } = new();
    [ObservableProperty] private LineupMemberViewModel? _selectedMember;
    [ObservableProperty] private object? _catalogPreview;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNameMissing))] private string _name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAuthorMissing))] private string _author;
    [ObservableProperty] private bool _showRequiredFieldErrors;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FilteredStrategyOptions))] private string _strategyQuery = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isDirty;
    public ObservableCollection<Operator> AvailableOperators { get; } = [];
    private readonly ObservableCollection<LineupMemberViewModel> _members = [];
    public ReadOnlyObservableCollection<LineupMemberViewModel> Members { get; }
    public IReadOnlyList<string> EquipmentNames { get; }
    public IReadOnlyList<Operator> CatalogOperators => _operators;
    public IReadOnlyList<LineupStrategyOption> StrategyOptions { get; }
    public IReadOnlyList<LineupCovenantOption> CovenantTagOptions { get; }
    public string CovenantTagSummary
    {
        get
        {
            var count = CovenantTagOptions.Count(option => option.IsSelected);
            return count == 0 ? "盟约标签" : $"盟约标签（已选 {count}）";
        }
    }
    public IEnumerable<LineupStrategyOption> FilteredStrategyOptions => StrategyOptions.Where(option =>
        option.Strategy.Name.Contains(StrategyQuery.Trim(), StringComparison.OrdinalIgnoreCase) ||
        option.Strategy.Sponsor.Contains(StrategyQuery.Trim(), StringComparison.OrdinalIgnoreCase));
    public string? FindStrategyIcon(string name) => StrategyOptions
        .FirstOrDefault(option => option.Strategy.Name == name)?.Strategy.IconPath;
    public string? FindIcon(bool equipment, string name) => equipment
        ? _equipment.GetValueOrDefault(name)?.IconPath
        : _operators.FirstOrDefault(op => op.Name == name)?.IconPath;
    public string MemberCount => $"已选 {Members.Count} / {Lineup.MaxMembers}";
    public bool CanAddOperator => Members.Count < Lineup.MaxMembers;
    public bool IsNameMissing => string.IsNullOrWhiteSpace(Name);
    public bool IsAuthorMissing => string.IsNullOrWhiteSpace(Author);

    public LineupEditorViewModel(IEnumerable<Operator> operators, IEnumerable<Equipment> equipment, Lineup? lineup = null,
        IEnumerable<Strategy>? strategies = null, IEnumerable<string>? covenantTags = null)
    {
        _operators = operators.OrderBy(o => o.Tier).ThenBy(o => o.Name).ToList();
        var gear = equipment.ToList();
        _equipment = gear.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _share = LineupShareService.FromCatalog(_operators, gear.Select(e => e.Name));
        EquipmentNames = ["", .. gear.OrderBy(e => e.Tier).ThenBy(e => e.Name).Select(e => e.Name).Distinct()];
        _id = lineup?.Id ?? Guid.NewGuid();
        _isImported = lineup?.IsImported ?? false;
        _publishedLink = lineup?.PublishedLink;
        _name = lineup?.Name ?? "";
        _author = lineup?.Author ?? "";
        _description = lineup?.Description ?? "";
        var savedStrategies = lineup?.AvailableStrategies ?? [];
        var selectedStrategies = savedStrategies.ToHashSet(StringComparer.Ordinal);
        var catalog = (strategies ?? []).ToDictionary(strategy => strategy.Name, StringComparer.Ordinal);
        StrategyOptions = savedStrategies.Select(name => catalog.GetValueOrDefault(name)
                ?? new Strategy { Name = name, Description = "当前策略目录中缺失" })
            .Concat(catalog.Values.Where(strategy => !selectedStrategies.Contains(strategy.Name)))
            .Select(strategy => new LineupStrategyOption(strategy, selectedStrategies.Contains(strategy.Name))).ToArray();
        foreach (var option in StrategyOptions) option.PropertyChanged += (_, _) => IsDirty = true;
        var savedTags = lineup?.CovenantTags ?? [];
        var covenantNames = covenantTags ?? _operators.SelectMany(op => op.CoreCovenants.Concat(op.AdditionalCovenants));
        CovenantTagOptions = savedTags.Concat(covenantNames).Distinct(StringComparer.Ordinal)
            .Select(name => new LineupCovenantOption(name, savedTags.Contains(name))).ToArray();
        foreach (var option in CovenantTagOptions)
            option.PropertyChanged += (_, _) => { IsDirty = true; OnPropertyChanged(nameof(CovenantTagSummary)); };
        Members = new(_members);
        OperatorBrowser.LoadOperators(_operators);
        EquipmentBrowser.LoadEquipment(gear);
        OperatorBrowser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperatorBrowseViewModel.FilteredOperators)) RefreshAvailable();
        };
        foreach (var member in lineup?.Members ?? []) AddMember(member);
        SelectedMember = Members.FirstOrDefault();
        RefreshAvailable();
        IsDirty = false;
    }

    partial void OnNameChanged(string value) => IsDirty = true;
    partial void OnAuthorChanged(string value) => IsDirty = true;
    partial void OnDescriptionChanged(string value) => IsDirty = true;
    partial void OnSelectedMemberChanged(LineupMemberViewModel? value) => CatalogPreview = null;
    partial void OnSearchTextChanged(string value) => OperatorBrowser.SearchText = value;

    public void AddOperator(Operator op)
    {
        if (!_operators.Any(o => o.Name == op.Name)) return;
        if (Members.FirstOrDefault(m => m.OperatorName == op.Name) is { } existing)
        {
            SelectedMember = existing;
            Status = "该干员已在阵容中，已选中以便编辑。";
            return;
        }
        if (!CanAddOperator) { Status = $"阵容最多容纳 {Lineup.MaxMembers} 名干员，请先移除一名。"; return; }
        AddMember(new() { OperatorName = op.Name });
        SelectedMember = Members.Last();
        ChangedMembers();
    }

    private void AddMember(LineupMember member)
    {
        var vm = new LineupMemberViewModel(member, _operators.FirstOrDefault(o => o.Name == member.OperatorName), _equipment);
        vm.PropertyChanged += MemberChanged;
        _members.Add(vm);
    }

    private void MemberChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => IsDirty = true;

    public void RemoveMember(LineupMemberViewModel member)
    {
        var index = _members.IndexOf(member);
        if (!_members.Remove(member)) return;
        member.PropertyChanged -= MemberChanged;
        if (ReferenceEquals(SelectedMember, member))
            SelectedMember = Members.Count == 0 ? null : Members[Math.Min(index, Members.Count - 1)];
        ChangedMembers();
    }

    public bool Equip(LineupMemberViewModel member, string equipmentName, int? slot = null)
    {
        if (!Members.Contains(member) || !_equipment.ContainsKey(equipmentName) ||
            (slot is int number && (number < 1 || number > member.EquipmentSlots.Count)))
            return false;
        SelectedMember = member;
        var target = slot is int index ? member.EquipmentSlots[index - 1] :
            member.EquipmentSlots.FirstOrDefault(item => !item.HasEquipment);
        if (target is null)
        {
            Status = "该干员的装备槽已满，请拖到指定装备槽替换，或先清除装备。";
            return false;
        }
        target.Name = equipmentName;
        CatalogPreview = null;
        Status = $"已为{member.OperatorName}装备{equipmentName}。";
        return true;
    }

    public void ClearEquipment(LineupMemberViewModel member, int slot)
    {
        if (!Members.Contains(member) || slot < 1 || slot > member.EquipmentSlots.Count) return;
        SelectedMember = member;
        member.EquipmentSlots[slot - 1].Name = "";
        Status = "装备已清除。";
    }

    private void ChangedMembers()
    {
        IsDirty = true;
        Status = "";
        OnPropertyChanged(nameof(MemberCount));
        OnPropertyChanged(nameof(CanAddOperator));
        RefreshAvailable();
    }

    private void RefreshAvailable()
    {
        var selected = Members.Select(m => m.OperatorName).ToHashSet(StringComparer.Ordinal);
        var desired = OperatorBrowser.FilteredOperators.Where(o => !selected.Contains(o.Name)).ToList();
        var keep = desired.ToHashSet();
        for (var i = AvailableOperators.Count - 1; i >= 0; i--)
            if (!keep.Contains(AvailableOperators[i])) AvailableOperators.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < AvailableOperators.Count && ReferenceEquals(AvailableOperators[i], desired[i])) continue;
            var existing = AvailableOperators.IndexOf(desired[i]);
            if (existing >= 0) AvailableOperators.Move(existing, i);
            else AvailableOperators.Insert(i, desired[i]);
        }
    }

    public Lineup BuildLineup(bool includeDescription = true)
    {
        var tags = CovenantTagOptions.Where(option => option.IsSelected).Select(option => option.Name).ToList();
        var lineup = new Lineup
        {
            Id = _id, Name = Name.Trim(), Author = Author.Trim(), Description = includeDescription ? Description : "", IsImported = _isImported,
            PublishedLink = _publishedLink,
            AvailableStrategies = StrategyOptions.Where(option => option.IsSelected).Select(option => option.Strategy.Name).ToList(),
            CovenantTags = tags.Count > 0 ? tags : null,
            Members = Members.Select(m => m.Snapshot()).ToList()
        };
        _share.Validate(lineup);
        return lineup;
    }

    public string ExportLineup(Lineup lineup) => _share.Export(lineup);
}
