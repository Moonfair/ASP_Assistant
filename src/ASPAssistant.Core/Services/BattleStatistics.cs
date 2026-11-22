using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Services;

public sealed record OperatorCounters(double Physical, double Arts, double TrueDamage, double Elemental)
{
    [JsonIgnore] public double Damage => Physical + Arts + TrueDamage + Elemental;
}

public sealed record BattleFrame(int Round, uint Frame, double GameSeconds, bool Ended,
    Dictionary<string, OperatorCounters> Operators);

public sealed record BattleOperatorRow(string Id, OperatorCounters Counters, double DamageShare);

public sealed class BattleStatistics
{
    public double TotalDamage { get; private set; }
    public IReadOnlyList<BattleOperatorRow> Rows { get; private set; } = [];

    public void Reset()
    {
        TotalDamage = 0;
        Rows = [];
    }

    public void Update(BattleFrame frame)
    {
        TotalDamage = frame.Operators.Values.Sum(counters => counters.Damage);
        Rows = frame.Operators
            .OrderByDescending(pair => pair.Value.Damage)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new BattleOperatorRow(pair.Key, pair.Value,
                TotalDamage > 0 ? pair.Value.Damage / TotalDamage : 0))
            .ToArray();
    }
}
