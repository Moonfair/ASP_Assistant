using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>
/// 阵容槽位标签：玩家手动给某个干员标记，仅作分类/筛选/可视化用，不影响盟约激活计算。
/// </summary>
public enum LineupTag
{
    叠层,
    输出,
    控场,
    经济,
}

public class LineupSlot
{
    [JsonPropertyName("operatorName")]
    public string OperatorName { get; set; } = "";

    /// <summary>装备名列表，最多 2 个。</summary>
    [JsonPropertyName("equipments")]
    public List<string> Equipments { get; set; } = [];

    /// <summary>标签列表，最多 2 个。</summary>
    [JsonPropertyName("tags")]
    public List<LineupTag> Tags { get; set; } = [];
}

public class Lineup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>阵容内的干员槽位（最多 9 个）。</summary>
    [JsonPropertyName("slots")]
    public List<LineupSlot> Slots { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 单条盟约定义（来自 data/covenants.json，可被 covenants.user.json 覆盖）。
/// </summary>
public class CovenantInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>"SEASON" = 核心盟约（赛季限定）；"REGULAR" = 普通附加盟约。</summary>
    [JsonPropertyName("bondType")]
    public string BondType { get; set; } = "REGULAR";

    /// <summary>激活该盟约所需的同盟约干员数；null 表示数据缺失，应在 user 配置中补齐。</summary>
    [JsonPropertyName("activateCount")]
    public int? ActivateCount { get; set; }
}

/// <summary>
/// 阵容某盟约的实时统计：用于 UI 展示「已激活/层数/阈值」。
/// </summary>
public record CovenantStat(
    string Name,
    string BondType,
    int Count,
    int? ActivateCount,
    bool Activated);
