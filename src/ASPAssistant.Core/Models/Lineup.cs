using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public sealed record Lineup
{
    public const int MaxDescriptionLength = 12_000;
    public const int MaxMembers = 30;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsImported { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublishedLink { get; init; }
    public List<LineupMember> Members { get; init; } = [];
    public List<string> AvailableStrategies { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? CovenantTags { get; init; }

    [JsonIgnore]
    public string Summary => string.Join(" · ", Members.Select(m => m.OperatorName));

    [JsonIgnore]
    public string CovenantTagSummary => string.Join(" · ", CovenantTags ?? []);
}

public sealed record LineupMember
{
    public string OperatorName { get; init; } = "";
    public bool IsStackingCore { get; init; }
    public bool IsDamageCore { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsElite { get; init; }
    public string Equipment1 { get; init; } = "";
    public string Equipment2 { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Equipment3 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Equipment4 { get; init; }

    [JsonIgnore]
    public string[] EquipmentSlots => [Equipment1, Equipment2, Equipment3 ?? "", Equipment4 ?? ""];
}
