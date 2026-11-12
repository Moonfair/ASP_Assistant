namespace ASPAssistant.Core.Models;

public sealed record CovenantPool(IReadOnlyList<string> CoreCovenants, IReadOnlyList<string> AdditionalCovenants)
{
    public bool SelectCoreGroup { get; init; }
}

public sealed class CovenantCatalog
{
    public List<string> CoreCovenants { get; init; } = [];
    public List<string> AdditionalCovenants { get; init; } = [];
    public Dictionary<string, CovenantPool> ModePools { get; init; } = [];
    public Dictionary<string, CovenantActivationRule> ActivationRules { get; init; } = [];
    public List<string> JobChangeActivators { get; init; } = [];
    public Dictionary<string, string> JobChangeEquipment { get; init; } = [];
}

public sealed record CovenantActivationRule(int MinimumCount, int? MaximumCount = null,
    bool RequiresElite = false, int CoreActivationBonus = 0)
{
    public bool IsMet(int count) => count >= MinimumCount && (MaximumCount is null || count <= MaximumCount);
}
