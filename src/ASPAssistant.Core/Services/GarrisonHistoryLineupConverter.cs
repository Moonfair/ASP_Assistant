using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed class GarrisonHistoryLineupConverter(
    IEnumerable<Operator> operators,
    IEnumerable<string> equipment,
    IEnumerable<string> strategies)
{
    private readonly Dictionary<string, int> _equipmentSlots =
        operators.ToDictionary(item => item.Name, item => item.EquipmentSlotCount, StringComparer.Ordinal);
    private readonly HashSet<string> _equipment = new(equipment, StringComparer.Ordinal);
    private readonly HashSet<string> _strategies = new(strategies, StringComparer.Ordinal);

    public GarrisonLineupExport Convert(GarrisonRecord record, GarrisonCatalog catalog)
    {
        var omissions = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<LineupMember>();
        var addedOperators = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in (record.Squad ?? []).OfType<GarrisonRecordMember>())
        {
            var name = Resolve(entry.CharId, catalog.Operators, _equipmentSlots.ContainsKey, "干员", omissions);
            if (name is null) continue;
            if (!addedOperators.Add(name))
            {
                omissions.Add($"跳过重复干员「{name}」。");
                continue;
            }
            if (members.Count == Lineup.MaxMembers)
            {
                omissions.Add($"阵容已按战绩顺序保留前 {Lineup.MaxMembers} 名可用干员。");
                break;
            }

            var slotCount = _equipmentSlots[name];
            var items = (entry.Equips ?? []).Take(slotCount)
                .Select(item => Resolve(item?.EquipId, catalog.Equipment, _equipment.Contains, "装备", omissions))
                .ToArray();
            if (entry.Equips?.Skip(slotCount).Any(item => !string.IsNullOrWhiteSpace(item?.EquipId)) == true)
                omissions.Add($"「{name}」最多可携带 {slotCount} 件装备，超出的装备未加入。");
            members.Add(new LineupMember
            {
                OperatorName = name,
                IsElite = entry.MergeLevel is > 0,
                Equipment1 = items.ElementAtOrDefault(0) ?? "",
                Equipment2 = items.ElementAtOrDefault(1) ?? "",
                Equipment3 = items.ElementAtOrDefault(2),
                Equipment4 = items.ElementAtOrDefault(3)
            });
        }

        var strategy = Resolve(record.StrategyId, catalog.Strategies, _strategies.Contains, "策略", omissions);
        if (members.Count == 0) return new GarrisonLineupExport(null, omissions.ToArray());

        var title = record.EndedAt is { } endedAt
            ? $"卫戍战绩 {endedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "卫戍历史阵容";
        var lineup = new Lineup
        {
            Name = title,
            Author = "本人",
            Members = members,
            AvailableStrategies = strategy is null ? [] : [strategy]
        };
        return new GarrisonLineupExport(lineup, omissions.ToArray());
    }

    private static string? Resolve(string? id, IReadOnlyDictionary<string, string> names,
        Func<string, bool> isAvailable, string kind, HashSet<string> omissions)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var name = names.GetValueOrDefault(id, id);
        if (isAvailable(name)) return name;
        omissions.Add($"当前数据中没有{kind}「{name}」。");
        return null;
    }
}

public sealed record GarrisonLineupExport(Lineup? Lineup, IReadOnlyList<string> Omissions);
