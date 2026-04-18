namespace ASPAssistant.Core.Models;

/// <summary>
/// Result of a single "识别本场敌人" pass.
/// Always contains exactly one boss and two enemy types (when recognition succeeds).
/// </summary>
public record EnemyResult(string BossName, IReadOnlyList<string> EnemyTypes);
