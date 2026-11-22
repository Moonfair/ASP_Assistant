using System.ComponentModel;

namespace ASPAssistant.Core.Services;

// Windows x64 2.7.91. ArknightsBattleReader.Open checks the executable, assembly and metadata hashes.
// Only the public account, team, scene and dialog fields below are sampled, individually.
internal sealed class GameRoomMemoryReader(long module, Func<long, int, byte[]> read,
    Func<long, string, string, bool> typeMatches, Func<long, string> readString)
{
    private const string Game = "Torappu.UI.AutoChess", Server = Game + ".Server";
    private const string Battle = "Torappu.Battle.AutoChess", ServiceCore = "Torappu.SocketNetwork.SvrCom";
    private const int Parent = 88, StaticFields = 184;
    private readonly Dictionary<(long Address, int Length), byte[]> _sample = [];

    internal GameRoomObservation? Read()
    {
        _sample.Clear();
        try
        {
            var result = Capture();
            // Scene changes, player-state packets, and page navigation can all occur during one poll.
            return result != null && _sample.All(p => p.Value.AsSpan().SequenceEqual(read(p.Key.Address, p.Key.Length)))
                ? result : null;
        }
        catch (InvalidDataException) { return null; }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 299 or 487 or 998) { return null; }
    }

    private GameRoomObservation? Capture()
    {
        // AutoChessPrepareModel.LoadData uses U8SDKInterface's public UID getter (instance + 104).
        long sdk = Singleton(0x8b03820, 0x20027ce5, "U8SDKInterface", "U8.SDK", "SDKInterfaceWIN");
        long player = Singleton(0x8b68e20, 0x20020123, "PlayerData", "Torappu", null, ("Singleton`1", "Torappu"));
        if (sdk == 0 || player == 0) return null;
        var model = ObjectAt(player + 32, "PlayerDataModel", "Torappu");
        var status = ObjectAt(model + 32, "PlayerStatus", "Torappu");
        var identity = new GameRoomObservation(IdAt(sdk + 104), NameAt(status + 16), null, null, "idle", []);

        // All three Start paths initialise this type reference. Stop disposes the mode and clears the
        // ServiceCoreBase singleton even if Unity still retains objects from the previous room.
        long service = Singleton(0x8c61c70, 0x20015cb7, "AutoChessService", Server, null,
            ("ServiceBase`2", ServiceCore), ("ServiceCoreBase`1", ServiceCore));
        if (service == 0) return ReadSoloQueue(identity);
        long mode = Pointer(service + 40);
        if (IsObject(mode, "AutoChessTrainingServiceMode", Server)) return identity;
        Require(IsObject(mode, "AutoChessServiceMode", Server));
        long battleServer = ObjectAt(mode + 40, "AutoChessBattleServer", Server);
        long battleInfo = ObjectAt(battleServer + 200, "AutoChessServiceBattleInfo", Server);
        long phase = Pointer(service + 24);
        if (Flag(battleInfo + 16))
            return IsObject(phase, "AutoChessServiceBattlePhase", Server)
                ? ReadBattle(identity, battleServer, battleInfo) : null;
        return IsObject(phase, "AutoChessServiceTeamPhase", Server) ? ReadTeam(identity, mode) : null;
    }

    private GameRoomObservation? ReadTeam(GameRoomObservation identity, long mode)
    {
        long team = ObjectAt(mode + 80, "AutoChessServiceTeamInfo", Server);
        int state = Number(team + 24);
        // NONE/END can retain the previous team while the service stops or switches servers.
        // IN_BATTLE waits for the new scene's first full sync.
        string? phase = state switch { 1 => "lobby", >= 2 and <= 7 or 10 => "matching", _ => null };
        if (phase == null) return null;
        long active = ObjectAt(mode + 48, "AutoChessTeamServer", Server);
        var roomId = IdAt(team + 144);
        // AutoChessRoomViewModel.RefreshData displays teamID. _UpdateTeamStatus fills it from activeEntry.id.
        Require(IdAt(active + 112) == roomId);
        var host = OptionalIdAt(team + 40);
        var list = ObjectAt(team + 64, "List`1", "System.Collections.Generic");
        var (array, count) = ListStorage(list, 16);
        var seats = new GameRoomSeat[count];
        for (int i = 0; i < count; i++)
        {
            var member = ObjectAt(array + 32 + 8 * i, "MsgAutoChessPlayerStatus", Server);
            var uid = IdAt(member + 16);
            int playerState = Number(member + 116);
            Require(playerState is >= 0 and <= 12);
            // AutoChessRoomPlayerCardViewModel.LoadData always displays the host as ready.
            seats[i] = new(uid, NameAt(member + 24), uid == host || playerState == 1);
        }
        Require(host == null || seats.Any(s => s.Uid == host));
        ValidateSeats(seats, identity.SelfUid);
        return identity with { RoomId = roomId, HostUid = host, Phase = phase, Members = seats };
    }

    private GameRoomObservation ReadBattle(GameRoomObservation identity, long server, long info)
    {
        // _HandleAllStateSync passes this battle server's own join-entry ID into BattleInfo.Fill(sceneId).
        // This path also runs on a direct reconnect when there is no team server or known lobby.
        string sceneId = IdAt(info + 32);
        Require(IdAt(server + 160) == sceneId);
        var scene = ObjectAt(info + 48, "AutoChessBattleSceneData", Game);
        var playersModule = ObjectAt(scene + 24, "AutoChessScenePlayerStaticData", "");
        var players = ObjectAt(playersModule + 24, "ScenePlayerStaticData", Battle);
        var table = ObjectAt(players + 16, "ListDict`2", "Torappu");
        var (array, count) = ListStorage(table, 16);
        var seats = new GameRoomSeat[count];
        var indices = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            long entry = array + 32 + 16 * i;
            int index = Number(entry);
            var member = ObjectAt(entry + 8, "StaticScenePlayerData", Battle);
            Require(indices.Add(index) && Number(member + 16) == index);
            var card = ObjectAt(member + 56, "PlayerCard", Battle);
            // The battle roster has no lobby Ready control or room owner.
            seats[i] = new(IdAt(member + 24), NameAt(card + 16), false);
        }
        ValidateSeats(seats, identity.SelfUid);
        return identity with { RoomId = sceneId, Phase = "playing", Members = seats };
    }

    private GameRoomObservation? ReadSoloQueue(GameRoomObservation identity)
    {
        // The solo queue opens a dialog on AutoChessPreparePage before the player joins any team.
        long pages = Singleton(0x8b47130, 0x20028559, "UIPageController", "Torappu.UI", null,
            ("SingletonMonoBehaviour`1", "Torappu"));
        if (pages == 0 || Pointer(pages + 16) == 0 || Flag(pages + 120)) return null;
        var stack = ObjectAt(pages + 104, "List`1", "System.Collections.Generic");
        var (stackArray, depth) = ListStorage(stack, 256);
        if (depth == 0) return null;
        // UIPageController._PeekPageStack returns the last 32-byte StackElmt; pageInst is its second word.
        long page = Pointer(stackArray + 40 + (depth - 1) * 32);
        Require(IsPointer(page) && IsPointer(Pointer(page + 16)));
        if (!IsObject(page, "AutoChessPreparePage", Game)) return identity;
        var manager = ObjectAt(page + 256, "UICompDialogMgr", "Torappu.UI");
        var dialogs = ObjectAt(manager + 16, "ListDict`2", "Torappu");
        var (dialogArray, count) = ListStorage(dialogs, 64);
        for (int i = 0; i < count; i++)
        {
            long entry = dialogArray + 32 + i * 16;
            var wrapper = ObjectAt(entry + 8, "DialogWrapper", "");
            if (Flag(wrapper + 16) || Flag(wrapper + 17)) continue;
            long dialog = Pointer(wrapper + 32);
            if (!IsObject(dialog, "AutoChessSoloMatchingDialog", Game)) continue;
            Require(Number(dialog + 80) == Number(entry));
            if (Flag(dialog + 48)) continue;
            var model = ObjectAt(dialog + 152, "AutoChessMultiMatchViewModel", Game);
            return Number(model + 20) switch
            {
                1 => identity with { Phase = "matching" },
                2 when Number(model + 24) == 2 => identity with { Phase = "matching" },
                2 => identity,
                _ => null
            };
        }
        return identity;
    }

    private long Singleton(int classRva, long unresolved, string name, string ns, string? concreteSubclass,
        params (string Name, string Namespace)[] parents)
    {
        long klass = Pointer(module + classRva);
        if (klass == unresolved) return 0;
        Require(IsPointer(klass) && typeMatches(klass, name, ns));
        long owner = klass;
        foreach (var parent in parents)
        {
            owner = Pointer(owner + Parent);
            Require(IsPointer(owner) && typeMatches(owner, parent.Name, parent.Namespace));
        }
        long storage = Pointer(owner + StaticFields);
        if (storage == 0) return 0;
        Require(IsPointer(storage));
        long obj = Pointer(storage);
        if (obj == 0) return 0;
        Require(IsPointer(obj));
        long actual = Pointer(obj);
        // U8SDKInterface.get_Instance allocates SDKInterfaceWIN on Windows and stores it in the base static.
        Require(concreteSubclass == null ? actual == klass :
            IsPointer(actual) && typeMatches(actual, concreteSubclass, ns) && Pointer(actual + Parent) == klass);
        return obj;
    }

    private (long Array, int Count) ListStorage(long list, int limit)
    {
        byte[] header = Bytes(list + 16, 16); // items, size and version
        var array = BitConverter.ToInt64(header);
        int count = BitConverter.ToInt32(header, 8);
        Require(count >= 0 && count <= limit);
        if (count > 0) Require(IsPointer(array) && Pointer(array + 24) >= count);
        return (array, count);
    }

    private static void ValidateSeats(GameRoomSeat[] seats, string selfUid) =>
        Require(seats.Select(s => s.Uid).Distinct(StringComparer.Ordinal).Count() == seats.Length &&
            seats.Any(s => s.Uid == selfUid));

    private bool IsObject(long obj, string name, string ns) =>
        IsPointer(obj) && typeMatches(Pointer(obj), name, ns);

    private long ObjectAt(long address, string name, string ns)
    {
        long obj = Pointer(address);
        Require(IsObject(obj, name, ns));
        return obj;
    }

    private string NameAt(long address)
    {
        long value = Pointer(address);
        Require(IsPointer(value));
        string name = readString(value);
        Require(!string.IsNullOrWhiteSpace(name));
        return name;
    }

    private string IdAt(long address) =>
        OptionalIdAt(address) ?? throw new InvalidDataException("游戏标识尚未就绪。");

    private string? OptionalIdAt(long address)
    {
        long value = Pointer(address);
        if (value == 0) return null;
        Require(IsPointer(value));
        string id = readString(value);
        Require(!id.Any(char.IsWhiteSpace));
        return id.Length == 0 ? null : id;
    }

    private bool Flag(long address)
    {
        byte flag = Bytes(address, 1)[0];
        Require(flag <= 1);
        return flag == 1;
    }

    private static void Require(bool valid)
    {
        if (!valid) throw new InvalidDataException("游戏房间数据正在变化。");
    }
    private static bool IsPointer(long value) => value is >= 0x10000 and < 0x7fffffffffff && (value & 7) == 0;
    private int Number(long address) => BitConverter.ToInt32(Bytes(address, 4));
    private long Pointer(long address) => BitConverter.ToInt64(Bytes(address, 8));
    private byte[] Bytes(long address, int length)
    {
        if (!_sample.TryGetValue((address, length), out var data))
            _sample.Add((address, length), data = read(address, length));
        return data;
    }
}
