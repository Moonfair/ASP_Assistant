using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public class TraitEntry
{
    [JsonPropertyName("traitType")]
    public string TraitType { get; set; } = "";

    [JsonPropertyName("traitDescription")]
    public string TraitDescription { get; set; } = "";
}

public class OperatorVariant
{
    [JsonPropertyName("traits")]
    public List<TraitEntry> Traits { get; set; } = [];
}

public class Operator
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("strategyOnly")]
    public bool StrategyOnly { get; set; }

    [JsonPropertyName("selfSelected")]
    public bool SelfSelected { get; set; }

    [JsonIgnore]
    public bool CanBecomeElite => Tier > 0 || SelfSelected;

    [JsonPropertyName("equipmentSlotCount")]
    public int EquipmentSlotCount { get; set; } = 2;

    [JsonIgnore]
    public string TierLabel => Tier == 0 ? "特殊" : Tier.ToString();

    [JsonPropertyName("coreCovenants")]
    public List<string> CoreCovenants { get; set; } = [];

    /// <summary>Primary core covenant (first entry), empty string if none.</summary>
    [JsonIgnore]
    public string CoreCovenant => CoreCovenants.FirstOrDefault() ?? "";

    [JsonPropertyName("additionalCovenants")]
    public List<string> AdditionalCovenants { get; set; } = [];

    [JsonPropertyName("normal")]
    public OperatorVariant Normal { get; set; } = new();

    [JsonPropertyName("elite")]
    public OperatorVariant Elite { get; set; } = new();

    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; } = "";
}
