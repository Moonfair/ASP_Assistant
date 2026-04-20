using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>
/// 阵容编辑会话的临时 view-model：固定持有 9 个槽位，未填的槽位 OperatorName 为空。
/// 所有修改后调用 <see cref="Recalculate"/> 刷新激活盟约统计。
/// </summary>
public partial class LineupEditorViewModel : ObservableObject
{
    public const int MaxSlots = 9;
    public const int MaxEquipPerSlot = 2;
    public const int MaxTagsPerSlot = 2;

    private readonly CovenantCalculator _calculator;

    public IReadOnlyList<Operator> AllOperators { get; }
    public IReadOnlyList<Equipment> AllEquipment { get; }

    public string LineupId { get; }

    [ObservableProperty]
    private string _name = "新阵容";

    /// <summary>9 个槽位，空槽位的 OperatorName 为空字符串。</summary>
    [ObservableProperty]
    private ObservableCollection<LineupSlot> _slots = [];

    /// <summary>仅展示当前阵容内出现过的盟约（Count &gt; 0）。</summary>
    [ObservableProperty]
    private ObservableCollection<CovenantStat> _activeStats = [];

    [ObservableProperty]
    private int _activatedCount;

    [ObservableProperty]
    private int _filledSlotCount;

    public LineupEditorViewModel(
        Lineup? source,
        IEnumerable<Operator> operators,
        IEnumerable<Equipment> equipment,
        CovenantCalculator calculator)
    {
        _calculator = calculator;
        AllOperators = [.. operators];
        AllEquipment = [.. equipment];

        LineupId = source?.Id ?? Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(source?.Name))
            _name = source!.Name;

        var slots = new List<LineupSlot>(MaxSlots);
        if (source is not null)
            slots.AddRange(source.Slots.Take(MaxSlots).Select(CloneSlot));
        while (slots.Count < MaxSlots)
            slots.Add(new LineupSlot());
        _slots = new ObservableCollection<LineupSlot>(slots);

        Recalculate();
    }

    /// <summary>设置某个槽位的干员（空字符串 = 清空槽位，包括其装备/标签）。</summary>
    public void SetSlotOperator(int slotIndex, string operatorName)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;

        if (string.IsNullOrEmpty(operatorName))
        {
            Slots[slotIndex] = new LineupSlot();
        }
        else
        {
            var slot = Slots[slotIndex];
            slot.OperatorName = operatorName;
            // 触发 ItemsControl 刷新（LineupSlot 是 POCO，集合变更通知更可靠）
            Slots[slotIndex] = CloneSlot(slot);
        }
        Recalculate();
    }

    /// <summary>切换装备：若已装备则移除；若未装备则在不超过上限时追加。</summary>
    public void ToggleSlotEquipment(int slotIndex, string equipName)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;
        if (string.IsNullOrEmpty(equipName)) return;
        var slot = CloneSlot(Slots[slotIndex]);
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        if (slot.Equipments.Contains(equipName))
        {
            slot.Equipments.Remove(equipName);
        }
        else
        {
            if (slot.Equipments.Count >= MaxEquipPerSlot)
                slot.Equipments.RemoveAt(0); // 顶替最早的
            slot.Equipments.Add(equipName);
        }
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void ToggleSlotTag(int slotIndex, LineupTag tag)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;
        var slot = CloneSlot(Slots[slotIndex]);
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        if (slot.Tags.Contains(tag))
        {
            slot.Tags.Remove(tag);
        }
        else
        {
            if (slot.Tags.Count >= MaxTagsPerSlot)
                slot.Tags.RemoveAt(0);
            slot.Tags.Add(tag);
        }
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void Recalculate()
    {
        var snapshot = ToLineup();
        var allStats = _calculator.Compute(snapshot);
        var visible = allStats.Where(s => s.Count > 0)
                              .OrderByDescending(s => s.Activated)
                              .ThenBy(s => s.BondType == "SEASON" ? 0 : 1)
                              .ThenByDescending(s => s.Count)
                              .ThenBy(s => s.Name)
                              .ToList();
        ActiveStats = new ObservableCollection<CovenantStat>(visible);
        ActivatedCount = allStats.Count(s => s.Activated);
        FilledSlotCount = Slots.Count(s => !string.IsNullOrEmpty(s.OperatorName));
    }

    public Lineup ToLineup() => new()
    {
        Id = LineupId,
        Name = string.IsNullOrWhiteSpace(Name) ? "未命名阵容" : Name.Trim(),
        Slots = [.. Slots
            .Where(s => !string.IsNullOrEmpty(s.OperatorName))
            .Select(CloneSlot)],
        UpdatedAt = DateTime.UtcNow,
    };

    private static LineupSlot CloneSlot(LineupSlot s) => new()
    {
        OperatorName = s.OperatorName,
        Equipments   = [.. s.Equipments],
        Tags         = [.. s.Tags],
    };
}
