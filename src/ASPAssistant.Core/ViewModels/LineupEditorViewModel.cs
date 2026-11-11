using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

public partial class LineupMemberViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, Equipment> _equipment;
    public string OperatorName { get; }
    public Operator? Operator { get; }
    public string IconPath => Operator?.IconPath ?? "";
    public string Equipment1IconPath => _equipment.GetValueOrDefault(Equipment1 ?? "")?.IconPath ?? "";
    public string Equipment2IconPath => _equipment.GetValueOrDefault(Equipment2 ?? "")?.IconPath ?? "";
    public bool HasEquipment1 => !string.IsNullOrEmpty(Equipment1);
    public bool HasEquipment2 => !string.IsNullOrEmpty(Equipment2);
    [ObservableProperty] private bool _isStackingCore;
    [ObservableProperty] private bool _isDamageCore;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Equipment1IconPath)), NotifyPropertyChangedFor(nameof(HasEquipment1))]
    private string _equipment1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Equipment2IconPath)), NotifyPropertyChangedFor(nameof(HasEquipment2))]
    private string _equipment2;

    public LineupMemberViewModel(LineupMember member, Operator? op = null, IReadOnlyDictionary<string, Equipment>? equipment = null)
    {
        Operator = op;
        _equipment = equipment ?? new Dictionary<string, Equipment>();
        OperatorName = member.OperatorName;
        _isStackingCore = member.IsStackingCore;
        _isDamageCore = member.IsDamageCore;
        _equipment1 = member.Equipment1;
        _equipment2 = member.Equipment2;
    }

    public LineupMember Snapshot() => new()
    {
        OperatorName = OperatorName, IsStackingCore = IsStackingCore, IsDamageCore = IsDamageCore,
        Equipment1 = Equipment1 ?? "", Equipment2 = Equipment2 ?? ""
    };
}

public sealed partial class LineupStrategyOption(Strategy strategy, bool isSelected) : ObservableObject
{
    public Strategy Strategy { get; } = strategy;
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
    public IEnumerable<LineupStrategyOption> FilteredStrategyOptions => StrategyOptions.Where(option =>
        option.Strategy.Name.Contains(StrategyQuery.Trim(), StringComparison.OrdinalIgnoreCase) ||
        option.Strategy.Sponsor.Contains(StrategyQuery.Trim(), StringComparison.OrdinalIgnoreCase));
    public string? FindStrategyIcon(string name) => StrategyOptions
        .FirstOrDefault(option => option.Strategy.Name == name)?.Strategy.IconPath;
    public string? FindIcon(bool equipment, string name) => equipment
        ? _equipment.GetValueOrDefault(name)?.IconPath
        : _operators.FirstOrDefault(op => op.Name == name)?.IconPath;
    public string MemberCount => $"已选 {Members.Count} / 10";
    public bool CanAddOperator => Members.Count < 10;
    public bool IsNameMissing => string.IsNullOrWhiteSpace(Name);
    public bool IsAuthorMissing => string.IsNullOrWhiteSpace(Author);

    public LineupEditorViewModel(IEnumerable<Operator> operators, IEnumerable<Equipment> equipment, Lineup? lineup = null,
        IEnumerable<Strategy>? strategies = null)
    {
        _operators = operators.OrderBy(o => o.Tier).ThenBy(o => o.Name).ToList();
        var gear = equipment.ToList();
        _equipment = gear.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _share = new(_operators.Select(o => o.Name), gear.Select(e => e.Name));
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
        if (!CanAddOperator) { Status = "阵容最多容纳 10 名干员，请先移除一名。"; return; }
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
        if (!Members.Contains(member) || !_equipment.ContainsKey(equipmentName) || (slot is not null && slot is not (1 or 2)))
            return false;
        SelectedMember = member;
        slot ??= !member.HasEquipment1 ? 1 : !member.HasEquipment2 ? 2 : null;
        if (slot is null)
        {
            Status = "该干员的两个装备槽已满，请拖到指定装备槽替换，或先清除装备。";
            return false;
        }
        if (slot == 1) member.Equipment1 = equipmentName;
        else member.Equipment2 = equipmentName;
        CatalogPreview = null;
        Status = $"已为{member.OperatorName}装备{equipmentName}。";
        return true;
    }

    public void ClearEquipment(LineupMemberViewModel member, int slot)
    {
        if (!Members.Contains(member) || slot is not (1 or 2)) return;
        SelectedMember = member;
        if (slot == 1) member.Equipment1 = "";
        else member.Equipment2 = "";
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
        var lineup = new Lineup
        {
            Id = _id, Name = Name.Trim(), Author = Author.Trim(), Description = includeDescription ? Description : "", IsImported = _isImported,
            PublishedLink = _publishedLink,
            AvailableStrategies = StrategyOptions.Where(option => option.IsSelected).Select(option => option.Strategy.Name).ToList(),
            Members = Members.Select(m => m.Snapshot()).ToList()
        };
        _share.Validate(lineup);
        return lineup;
    }

    public string ExportLineup(Lineup lineup) => _share.Export(lineup);
}
