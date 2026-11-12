using ASPAssistant.Core.Models;
using ASPAssistant.Core.Data;

namespace ASPAssistant.Core.Services;

public sealed class LineupCovenantCalculator(CovenantCatalog catalog, IEnumerable<Operator> operators,
    OperatorCatalog? operatorCatalog = null)
{
    private readonly Dictionary<string, Operator> _operators = operators.ToDictionary(op => op.Name, StringComparer.Ordinal);
    private readonly HashSet<string> _core = new(catalog.CoreCovenants, StringComparer.Ordinal);
    private readonly HashSet<string> _activators = new(catalog.JobChangeActivators, StringComparer.Ordinal);

    public IReadOnlyList<string> GetActiveCovenants(Lineup lineup)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var eliteCount = 0;
        foreach (var member in lineup.Members.DistinctBy(member => member.OperatorName, StringComparer.Ordinal))
        {
            var op = _operators.GetValueOrDefault(member.OperatorName) ?? operatorCatalog?.Find(member.OperatorName);
            if (op is null) continue;
            var memberships = new HashSet<string>(op.CoreCovenants.Concat(op.AdditionalCovenants), StringComparer.Ordinal);
            var equipment = member.EquipmentSlots;
            if (equipment.Any(_activators.Contains))
            {
                foreach (var item in equipment)
                    if (catalog.JobChangeEquipment.TryGetValue(item, out var covenant))
                        memberships.Add(covenant);
            }
            foreach (var covenant in memberships)
                counts[covenant] = counts.GetValueOrDefault(covenant) + 1;
            if (member.IsElite && op.CanBecomeElite) eliteCount++;
        }

        var coreBonus = catalog.ActivationRules
            .Where(pair => pair.Value.IsMet(counts.GetValueOrDefault(pair.Key)))
            .Sum(pair => pair.Value.CoreActivationBonus);
        return catalog.ActivationRules.Where(pair =>
        {
            if (pair.Value.RequiresElite) return pair.Value.IsMet(eliteCount);
            var count = counts.GetValueOrDefault(pair.Key);
            if (count > 0 && _core.Contains(pair.Key)) count += coreBonus;
            return pair.Value.IsMet(count);
        }).Select(pair => pair.Key).ToArray();
    }
}
