using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public class OperatorVariant
{
    [JsonPropertyName("traitType")]
    public string TraitType { get; set; } = "";

    [JsonPropertyName("traitDescription")]
    public string TraitDescription { get; set; } = "";
}

public class Operator
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("coreCovenant")]
    public string CoreCovenant { get; set; } = "";

    [JsonPropertyName("additionalCovenants")]
    public List<string> AdditionalCovenants { get; set; } = [];

    [JsonPropertyName("normal")]
    public OperatorVariant Normal { get; set; } = new();

    [JsonPropertyName("elite")]
    public OperatorVariant Elite { get; set; } = new();
}
