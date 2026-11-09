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
}
