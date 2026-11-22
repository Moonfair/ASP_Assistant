using System.ComponentModel;

namespace ASPAssistant.Core.Services;

public sealed record GarrisonBond(string Id, int Characters, bool Active, int PersistentStacks, int TemporaryStacks);
public sealed record GarrisonTeamRoster(GarrisonCharacter[]? Characters, GarrisonItem[]? Equipment,
    GarrisonPosition[]? Positions, GarrisonBond[]? Bonds);
public sealed record GarrisonTeammate(int PlayerIndex, int? Health = null, int? ShopLevel = null, int? RawCoins = null,
    int? Streak = null, int? GameState = null, int? ConnectionState = null, GarrisonTeamRoster? Roster = null,
    GarrisonTeamRoster? Overview = null, Dictionary<string, string>? SelectedCharacters = null);

internal sealed partial class GarrisonMemoryReader
{
    private const string Server = "Torappu.UI.AutoChess.Server";

    private GarrisonTeammate[]? ReadTeam(long center, Dictionary<int, long>? players, int self)
    {
        var overview = TryStable(r => r.ReadOverview(center, self));
        if (players == null && overview == null) return null;
        var indices = new SortedSet<int>();
        if (players != null) indices.UnionWith(players.Keys);
        if (overview != null) indices.UnionWith(overview.Keys);
        indices.Remove(self);

        return indices.Select(index =>
        {
            var player = players?.GetValueOrDefault(index) ?? 0;
            var cached = overview?.GetValueOrDefault(index);
            var state = player == 0 ? null : TryStable(r => r.ReadTeammate(player, index, cached));
            return (state ?? new GarrisonTeammate(index)) with { Overview = cached };
        }).ToArray();
    }

    // A teammate can start spectating, leave, or receive a new overview during a poll.
    // Validate these optional sources independently so an unavailable roster doesn't erase the local HUD.
    private T? TryStable<T>(Func<GarrisonMemoryReader, T?> capture) where T : class
    {
        var section = new GarrisonMemoryReader(read, isObject, readId);
        try
        {
            var result = capture(section);
            return section.IsStable() ? result : null;
        }
        catch (InvalidDataException) { return null; }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 299 or 487 or 998) { return null; }
    }

    private GarrisonTeammate ReadTeammate(long player, int index, GarrisonTeamRoster? overview)
    {
        CheckPlayer(player, index);
        var stats = Bytes(player + 80, 44);
        var inventory = Pointer(player + 128);
        var roster = inventory == 0 ? null : TryStable(r => r.ReadPlayerRoster(inventory));
        var hasCustom = roster?.Characters?.Any(c => c.ChessId.Contains("_diy")) == true
            || overview?.Characters?.Any(c => c.ChessId.Contains("_diy")) == true;
        return new(index, BitConverter.ToInt32(stats, 0), BitConverter.ToInt32(stats, 8), BitConverter.ToInt32(stats, 12),
            BitConverter.ToInt32(stats, 24), BitConverter.ToInt32(stats, 36), BitConverter.ToInt32(stats, 40),
            roster, SelectedCharacters: hasCustom ? TryStable(r => r.SelectedCharacters(player)) : null);
    }

    private GarrisonTeamRoster ReadPlayerRoster(long inventory)
    {
        CheckType(inventory, "PlayerBattleData", Chess);
        return new(Collection(Pointer(inventory + 16), "CharChess", ReadCharacter),
            Collection(Pointer(inventory + 24), "EquipChess", ReadItem),
            Collection(Pointer(inventory + 40), "ChessPositionInfo", ReadPosition),
            Collection(Pointer(inventory + 48), "GarrisonBond", p => new GarrisonBond(
                readId(Pointer(p + 16)), Number(p + 24), Bytes(p + 28, 1)[0] != 0, Number(p + 32), Number(p + 36))));
    }

    private Dictionary<int, GarrisonTeamRoster>? ReadOverview(long center, int self)
    {
        var model = Pointer(center + 288);
        if (model == 0) return null;
        CheckType(model, "AutoChessQuickViewDataModel", Chess);
        var entries = IndexedObjects(Pointer(model + 24));
        if (entries == null) return null;
        entries.Remove(self);
        var results = new Dictionary<int, GarrisonTeamRoster>();
        if (entries.Count == 0) return results;

        var indexer = Pointer(center + 296);
        var chessIds = TryStable(r => r.IdentifierTable(indexer, 48));
        var bondIds = TryStable(r => r.IdentifierTable(indexer, 32));
        foreach (var (index, content) in entries)
        {
            if (content == 0) continue;
            var roster = TryStable(r => r.ReadOverviewRoster(index, content, chessIds, bondIds));
            if (roster != null) results.Add(index, roster);
        }
        return results;
    }

    private GarrisonTeamRoster ReadOverviewRoster(int index, long content, Dictionary<int, long>? chessIds, Dictionary<int, long>? bondIds)
    {
        CheckType(content, "AutoChessBattleQuickViewContent", Server);
        if (Number(content + 16) != index) throw new InvalidDataException("队伍概览玩家已变化");
        return new(Collection(Pointer(content + 24), "AutoChessBattleCharChess", p => new GarrisonCharacter(
                Number(p + 16), Resolve(chessIds, Number(p + 32)), EquipmentSlots(Pointer(p + 24)), Number(p + 40)), elementNamespace: Server),
            Collection(Pointer(content + 32), "AutoChessBattleEquipOrTrapChess", p => new GarrisonItem(
                Number(p + 16), Resolve(chessIds, Number(p + 20))), elementNamespace: Server),
            null, Collection(Pointer(content + 40), "AutoChessBattleChessBondInfo", p => new GarrisonBond(
                Resolve(bondIds, Number(p + 16)), Number(p + 28), Bytes(p + 32, 1)[0] != 0, Number(p + 20), Number(p + 24)),
                elementNamespace: Server));
    }

    private string Resolve(Dictionary<int, long>? ids, int id) =>
        ids != null && ids.TryGetValue(id, out var text) ? readId(text) : $"编号 {id}";

    private Dictionary<int, long>? IdentifierTable(long indexer, int field)
    {
        if (indexer == 0) return null;
        CheckType(indexer, "AutoChessDataIndexer", "Torappu.Battle.DataCenter");
        var dict = Pointer(indexer + field);
        if (dict == 0) return null;
        CheckType(dict, "Dictionary`2", Generic);
        var array = Pointer(dict + 24);
        var count = Number(dict + 32);
        if (count is < 0 or > 4096 || (count > 0 && (array == 0 || Pointer(array + 24) < count)))
            throw new InvalidDataException("队伍概览编号表已变化");
        var result = new Dictionary<int, long>();
        // Dictionary<int,string>.Entry is 24 bytes on this build. Keep each process read under 2 KiB.
        const int stride = 24, batch = 80;
        for (var start = 0; start < count; start += batch)
        {
            var raw = Bytes(array + 32 + start * stride, Math.Min(count - start, batch) * stride);
            for (var i = 0; i < raw.Length; i += stride)
            {
                var value = BitConverter.ToInt64(raw, i + 16);
                if (BitConverter.ToInt32(raw, i) >= 0 && value != 0) result[BitConverter.ToInt32(raw, i + 8)] = value;
            }
        }
        return result;
    }
}
