using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public sealed record Lineup
{
    public const int MaxDescriptionLength = 12_000;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsImported { get; init; }
    public List<LineupMember> Members { get; init; } = [];

    [JsonIgnore]
    public string Summary => string.Join(" · ", Members.Select(m => m.OperatorName));
}

public sealed record LineupMember
{
    public string OperatorName { get; init; } = "";
    public bool IsStackingCore { get; init; }
    public bool IsDamageCore { get; init; }
    public string Equipment1 { get; init; } = "";
    public string Equipment2 { get; init; } = "";
}
