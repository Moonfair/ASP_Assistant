namespace ASPAssistant.Core.Services;

public sealed record GarrisonCharacter(int InstanceId, string ChessId, int[]? EquipmentSlots, int SellPrice);
public sealed record GarrisonItem(int InstanceId, string ChessId);
public sealed record GarrisonPosition(int InstanceId, int Position, int Kind, int Direction);
public sealed record GarrisonOffer(string ChessId, int Slot, int Price, bool Frozen);
public sealed record GarrisonShop(int Level, int Coins, int UpgradeCost, int RefreshCost, int FreeRefreshes,
    GarrisonOffer[]? Offers);
public sealed record GarrisonSnapshot(int Round, int PlayerIndex, int? Health, int? PlayerLevel, int? PlayerCoins,
    GarrisonShop? Shop, GarrisonCharacter[]? Characters, GarrisonItem[]? Equipment, GarrisonItem[]? Magic,
    GarrisonPosition[]? Positions, string? ActivityId = null, Dictionary<string, string>? SelectedCharacters = null,
    GarrisonTeammate[]? Teammates = null, GarrisonPhase? Phase = null, BossDamageBoard? BossDamage = null);

// Windows 2.7.91 x64. ArknightsBattleReader validates the three build hashes before this layout is used.
// Paths come from BattleController.m_singletonHost and AutoChessDataCenter.get_selfPlayerData.
internal sealed partial class GarrisonMemoryReader(Func<long, int, byte[]> read, Func<long, string, string, bool> isObject,
    Func<long, string> readId)
{
    private const string Chess = "Torappu.Battle.AutoChess", Generic = "System.Collections.Generic";
    private readonly Dictionary<(long Address, int Length), byte[]> _sample = [];

    internal GarrisonSnapshot? Read(Func<(long Slot, long Controller)> findController)
    {
        _sample.Clear();
        var (slot, controller) = findController();
        if (controller == 0 || Pointer(controller + 16) == 0) return null;
        var mode = Pointer(controller + 376);
        if (mode == 0 || !isObject(mode, "AutoChessGameMode", "")) return null;
        var manager = FindManager(Pointer(controller + 744));
        if (manager == 0) return null;
        var center = Pointer(manager + 64);
        if (center == 0) return null;
        CheckType(center, "AutoChessDataCenter", "Torappu.Battle.DataCenter");
        var selfIndex = Number(center + 56);
        var round = Number(center + 60);
        var players = PlayerTable(Pointer(center + 248));
        var player = players?.GetValueOrDefault(selfIndex) ?? 0;
        if (player != 0) CheckPlayer(player, selfIndex);
        var inventory = player == 0 ? 0 : Pointer(player + 128);
        if (inventory != 0) CheckType(inventory, "PlayerBattleData", Chess);

        var snapshot = new GarrisonSnapshot(round, selfIndex,
            player == 0 ? null : Number(player + 80), player == 0 ? null : Number(player + 88),
            player == 0 ? null : Number(player + 92), ReadShop(Pointer(center + 256)),
            inventory == 0 ? null : Collection(Pointer(inventory + 16), "CharChess", ReadCharacter),
            inventory == 0 ? null : Collection(Pointer(inventory + 24), "EquipChess", ReadItem),
            inventory == 0 ? null : Collection(Pointer(inventory + 32), "EquipChess", ReadItem),
            inventory == 0 ? null : Collection(Pointer(inventory + 40), "ChessPositionInfo", ReadPosition),
            Identifier(center + 16));
        if (player != 0 && (snapshot.Characters?.Any(c => c.ChessId.Contains("_diy")) == true
            || snapshot.Shop?.Offers?.Any(c => c.ChessId.Contains("_diy")) == true))
        {
            snapshot = snapshot with { SelectedCharacters = SelectedCharacters(player) };
        }
        snapshot = snapshot with { Teammates = ReadTeam(center, players, selfIndex),
            Phase = TryStable(r => r.ReadPhase(manager, center)) };
        if (snapshot.Phase?.State == GarrisonPhase.BossBattle)
            snapshot = snapshot with { BossDamage = TryStable(r => r.ReadBossDamage(manager, center, players)) };

        // A purchase can mutate a slot in place without changing the round or list identity.
        if (!IsStable() || findController() != (slot, controller))
            throw new InvalidDataException("卫戍协议数据正在变化");
        return snapshot;
    }

    private bool IsStable() => _sample.All(pair => pair.Value.AsSpan().SequenceEqual(read(pair.Key.Address, pair.Key.Length)));

    private Dictionary<string, string>? SelectedCharacters(long player)
    {
        var slots = Collection(Pointer(player + 56), "SquadSlot", p => (Key: Identifier(p + 16), Value: Identifier(p + 24)));
        if (slots == null) return null;
        var selected = new Dictionary<string, string>();
        foreach (var (key, value) in slots)
            if (key != null && value != null) selected[key] = value;
        return selected;
    }

    private long FindManager(long host)
    {
        if (host == 0) return 0;
        CheckType(host, "SingletonHost", "");
        var store = Pointer(host + 16);
        if (store == 0) return 0;
        CheckType(store, "Dictionary`2", Generic);
        // Dictionary<Type, object>.Entry on x64: hash, next, key, value.
        var entries = ArrayBytes(Pointer(store + 24), Number(store + 32), 24, 80);
        for (int i = 0; i < entries.Length; i += 24)
        {
            if (BitConverter.ToInt32(entries, i) < 0 || BitConverter.ToInt64(entries, i + 8) == 0) continue;
            var value = BitConverter.ToInt64(entries, i + 16);
            if (value != 0 && isObject(value, "AutoChessGameManager", Chess)) return value;
        }
        return 0;
    }

    private Dictionary<int, long>? PlayerTable(long model)
    {
        if (model == 0) return null;
        CheckType(model, "AutoChessPlayerDataModel", Chess);
        return IndexedObjects(Pointer(model + 24));
    }

    private Dictionary<int, long>? IndexedObjects(long list)
    {
        if (list == 0) return null;
        CheckType(list, "ListDict`2", "Torappu");
        var pairs = ListBytes(list, 16, 32);
        var result = new Dictionary<int, long>();
        for (int i = 0; i < pairs.Length; i += 16)
        {
            if (!result.TryAdd(BitConverter.ToInt32(pairs, i), BitConverter.ToInt64(pairs, i + 8)))
                throw new InvalidDataException("玩家索引重复");
        }
        return result;
    }

    private void CheckPlayer(long player, int index)
    {
        CheckType(player, "ScenePlayerData", "");
        if (Number(player + 16) != index) throw new InvalidDataException("玩家索引已变化");
    }

    private GarrisonShop? ReadShop(long shop)
    {
        if (shop == 0) return null;
        CheckType(shop, "AutoChessShopDataModel", Chess);
        return new(Number(shop + 24), Number(shop + 28), Number(shop + 32), Number(shop + 36),
            Number(shop + 44), Collection(Pointer(shop + 56), "ChessGoods", p =>
                new GarrisonOffer(Identifier(p + 16) ?? "", Number(p + 24), Number(p + 28), Bytes(p + 32, 1)[0] != 0),
                allowEmptySlots: true));
    }

    private GarrisonCharacter ReadCharacter(long p)
    {
        return new(Number(p + 16), readId(Pointer(p + 32)), EquipmentSlots(Pointer(p + 24)), Number(p + 44));
    }

    private int[]? EquipmentSlots(long list)
    {
        if (list == 0) return null;
        CheckType(list, "List`1", Generic);
        var raw = ListBytes(list, 4, 64);
        var slots = new int[raw.Length / 4];
        Buffer.BlockCopy(raw, 0, slots, 0, raw.Length);
        return slots;
    }

    private GarrisonItem ReadItem(long p) => new(Number(p + 16), readId(Pointer(p + 24)));
    private GarrisonPosition ReadPosition(long p) => new(Number(p + 16), Number(p + 20), Number(p + 24), Number(p + 28));

    private T[]? Collection<T>(long list, string elementName, Func<long, T> element, bool allowEmptySlots = false,
        string elementNamespace = Chess)
    {
        if (list == 0) return null;
        CheckType(list, "List`1", Generic);
        var entries = ListBytes(list, 8, 256);
        var values = new List<T>(entries.Length / 8);
        for (var i = 0; i < entries.Length; i += 8)
        {
            var p = BitConverter.ToInt64(entries, i);
            if (allowEmptySlots && p == 0) continue;
            CheckType(p, elementName, elementNamespace);
            values.Add(element(p));
        }
        return values.ToArray();
    }

    private string? Identifier(long address)
    {
        var p = Pointer(address);
        return p == 0 ? null : readId(p);
    }

    private byte[] ListBytes(long list, int stride, int max) =>
        ArrayBytes(Pointer(list + 16), Number(list + 24), stride, max);

    private byte[] ArrayBytes(long array, int count, int stride, int max)
    {
        if (count < 0 || count > max) throw new InvalidDataException("集合数量异常");
        if (count == 0) return [];
        if (array == 0 || Pointer(array + 24) < count) throw new InvalidDataException("集合已变化");
        return Bytes(array + 32, count * stride);
    }

    private int Number(long address) => BitConverter.ToInt32(Bytes(address, 4));
    private long Pointer(long address) => BitConverter.ToInt64(Bytes(address, 8));
    private byte[] Bytes(long address, int length)
    {
        if (!_sample.TryGetValue((address, length), out var value))
            _sample[(address, length)] = value = read(address, length);
        return value;
    }

    private void CheckType(long obj, string name, string ns)
    {
        if (!isObject(obj, name, ns)) throw new InvalidDataException("卫戍协议对象类型已变化");
    }
}
