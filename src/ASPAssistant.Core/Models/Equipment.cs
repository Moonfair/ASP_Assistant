using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public enum EquipmentType { Normal, JobChange, Exclusive }

public class EquipmentVariant
{
    [JsonPropertyName("effectDescription")]
    public string EffectDescription { get; set; } = "";
}

public class Equipment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("normal")]
    public EquipmentVariant Normal { get; set; } = new();

    [JsonPropertyName("elite")]
    public EquipmentVariant Elite { get; set; } = new();

    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; } = "";

    [JsonIgnore]
    public EquipmentType Type { get; set; } = EquipmentType.Normal;
}
