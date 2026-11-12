namespace ASPAssistant.Core.Models;

public sealed record CatalogDetails(
    string? Season,
    Dictionary<string, List<OperatorSkill>> Skills,
    CatalogTips? Tips);

public sealed record OperatorSkill(int Slot, string Name, string Level, string Description,
    string Recovery, string Activation, int SpCost, int InitialSp, double Duration, int Charges,
    string? IconPath = null, double RecoveryPerSecond = 0)
{
    public IEnumerable<string> Tags
    {
        get
        {
            if (Activation != "被动")
            {
                yield return $"技力消耗 {SpCost}";
                if (SpCost > 0 && RecoveryPerSecond > 0 && Recovery == "自动回复")
                    yield return $"基础 CD {SpCost / RecoveryPerSecond:0.##} 秒";
                yield return $"初始技力 {InitialSp}";
                if (Duration > 0) yield return $"持续 {Duration:0.##} 秒";
                if (Charges > 1 && SpCost > 0) yield return $"技力上限 {SpCost * Charges}";
                if (!string.IsNullOrEmpty(Recovery)) yield return Recovery;
            }
            yield return Activation;
            yield return Level;
        }
    }
}

public sealed record CatalogTips(string Season, string? Version, string Title, string Url,
    Dictionary<string, CatalogTip> Operators, Dictionary<string, CatalogTip> Equipment);

public sealed record CatalogTip(int Seconds, string Text);
