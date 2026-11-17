namespace ASPAssistant.Core.Models;

public sealed record PreparationEnemyInfo(string? Boss, IReadOnlyList<string> EnemyTypes,
    int? Battlefield, string? Mode);
