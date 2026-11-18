using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public enum TrackingType
{
    Operator,
    Equipment
}

public class TrackingEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public TrackingType Type { get; set; }

    /// <summary>
    /// Which actions put this entry on the tracking list, e.g. "manual", "lineup:&lt;id&gt;",
    /// "covenant:&lt;core&gt;|&lt;additional&gt;". Multiple sources can claim the same entry;
    /// it only leaves the list when the last one releases it.
    /// </summary>
    [JsonPropertyName("origins")]
    public HashSet<string> Origins { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public string IconPath { get; internal set; } = "";
}
