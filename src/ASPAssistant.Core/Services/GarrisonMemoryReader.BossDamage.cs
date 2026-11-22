namespace ASPAssistant.Core.Services;

public sealed record BossDamageEntry(int PlayerIndex, string? PlayerName, string ChessId, int Damage);
public sealed record BossDamageBoard(int? MaxHp, BossDamageEntry[] Rows)
{
    // Match the in-game display: clamp the single-precision ratio and truncate to whole percent.
    public int? Percent(int damage) => MaxHp is > 0
        ? (int)(Math.Clamp((float)damage / MaxHp.Value, 0f, 1f) * 100f) : null;
}

internal sealed partial class GarrisonMemoryReader
{
    private BossDamageBoard? ReadBossDamage(long manager, long center, Dictionary<int, long>? players)
    {
        // PC 2.7.91: the game board uses battleStatus.bossDamageList, not BattleLogger's all-target counters.
        // Sample the current phase again in this optional section so its stability check covers the board.
        if (ReadPhase(manager, center)?.State != GarrisonPhase.BossBattle) return null;
        var status = Pointer(center + 240);
        if (status == 0) return null;
        CheckType(status, "AutoChessBattleStatusDataModel", Chess);
        var list = Pointer(status + 72);
        if (list == 0) return null;

        var boss = Pointer(center + 136);
        int? hp = boss != 0 && isObject(boss, "BossBattleData", Chess) && Number(boss + 16) is > 0 and var max
            ? max : null;
        var ids = TryStable(r => r.IdentifierTable(r.Pointer(center + 296), 48));
        var rows = Collection(list, "AutoChessBossDamageDataItemModel", p =>
        {
            var index = Number(p + 16);
            var chess = Number(p + 20);
            var damage = Number(p + 24);
            if (index < 0 || damage < 0) throw new InvalidDataException("领袖伤害榜正在更新");
            var player = players?.GetValueOrDefault(index) ?? 0;
            var name = player == 0 ? null : TryStable(r =>
            {
                r.CheckPlayer(player, index);
                var card = r.Pointer(player + 64);
                return card != 0 && isObject(card, "PlayerCard", Chess) ? r.Identifier(card + 16) : null;
            });
            return new BossDamageEntry(index, name, Resolve(ids, chess), damage);
        });
        return rows == null ? null : new(hp, rows);
    }
}
