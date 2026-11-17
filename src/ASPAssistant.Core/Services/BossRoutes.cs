using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public sealed record BossRoute(string ImageFile)
{
    public string Order(bool left) => left ? "L1 → L2 → L3 → L1" : "R1 → R2 → R3 → R1";
}

public static class BossRoutes
{
    public static IReadOnlyList<int> Battlefields { get; } = Array.AsReadOnly<int>([1, 2, 3, 4, 5, 6, 9, 10, 11, 12]);

    public static BossRoute? For(string? boss, string? mode, int battlefield, int round)
    {
        if (mode == "极速模拟" || !Battlefields.Contains(battlefield)) return null;
        if (round == 14 && boss == "矩兽") return new($"{battlefield:D2}-beast.jpg");
        if (boss is not ("假想敌：铳" or "假想敌：鳍")) return null;
        return round == 14 || (round == 15 && HasHiddenRound(mode))
            ? new($"{battlefield:D2}-patrol.jpg")
            : null;
    }

    public static bool HasHiddenRound(string? mode) =>
        mode is not ("标准模拟" or "极速模拟") &&
        (mode == null || !Regex.IsMatch(mode, @"^规程等级[1-4]$"));
}
