namespace ASPAssistant.Core.Models;

public sealed record CatalogDetails(
    string? Season,
    Dictionary<string, List<OperatorSkill>> Skills,
    CatalogTips? Tips);

public sealed record OperatorSkill(int Slot, string Name, string Level, string Description,
    string Recovery, string Activation, int SpCost, int InitialSp, double Duration, int Charges)
{
    public string Timing
    {
        get
        {
            if (Activation == "被动") return "被动";
            var parts = new List<string> { Activation };
            if (!string.IsNullOrEmpty(Recovery)) parts.Add(Recovery);
            parts.Add($"技力 {SpCost} · 初始 {InitialSp}");
            if (Duration > 0) parts.Add($"持续 {Duration:0.##} 秒");
            if (Charges > 1 && SpCost > 0) parts.Add($"技力上限 {SpCost * Charges}");
            return string.Join("　", parts);
        }
    }
}

public sealed record CatalogTips(string Season, string? Version, string Title, string Url,
    Dictionary<string, CatalogTip> Operators, Dictionary<string, CatalogTip> Equipment);

public sealed record CatalogTip(int Seconds, string Text);
