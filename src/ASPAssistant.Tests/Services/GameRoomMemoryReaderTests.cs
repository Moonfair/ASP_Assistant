using System.Text.Json;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public class GameRoomMemoryReaderTests
{
    [Fact]
    public void BattleFeedCanCarryAnIndependentRoomObservation()
    {
        var game = new GameRoomObservation("1001", "博士", "00482", "1001", "lobby", [new("1001", "博士", true)]);
        var update = new BattleUpdate(BattleFeedState.WaitingForRound, Room: game);
        var received = JsonSerializer.Deserialize<BattleUpdate>(JsonSerializer.Serialize(update));
        Assert.Equal(BattleFeedState.WaitingForRound, received?.State);
        Assert.Equal(game.RoomId, received?.Room?.RoomId);
        Assert.Equal(game.Members, received?.Room?.Members);
        Assert.Null(JsonSerializer.Deserialize<BattleUpdate>("""{"State":3}""")?.Room);
    }

    [Fact]
    public void ReadsIdentityOnAnOrdinaryPageBeforeTheRoomServiceWasEverStarted()
    {
        var memory = new Sample();
        var game = Assert.IsType<GameRoomObservation>(memory.Read());
        Assert.Equal(("1001", "博士甲", "idle"), (game.SelfUid, game.Nickname, game.Phase));
        Assert.Null(game.RoomId);
        Assert.Null(game.HostUid);
        Assert.Empty(game.Members);
    }

    [Fact]
    public void AnUnrelatedSdkObjectCannotSupplyAnAccountIdentity()
    {
        var memory = new Sample();
        memory.SetSdkWithWrongBase();
        Assert.Null(memory.Read());
    }

    [Fact]
    public void TracksGameHostAndGuestReadinessAndRequiresTheActiveTeamNumber()
    {
        var memory = new Sample();
        memory.EnterLobby();
        var lobby = Assert.IsType<GameRoomObservation>(memory.Read());
        Assert.Equal(("00482", "2002", "lobby"), (lobby.RoomId, lobby.HostUid, lobby.Phase));
        Assert.Equal([new("2002", "队友乙", true), new("1001", "博士甲", false)], lobby.Members);
        memory.Number(0x40000 + 116, 1);
        memory.StringField(0x37000 + 40, "1001");
        memory.Number(0x37000 + 24, 2);
        var matching = Assert.IsType<GameRoomObservation>(memory.Read());
        Assert.Equal(("1001", "matching"), (matching.HostUid, matching.Phase));
        Assert.All(matching.Members, seat => Assert.True(seat.Ready));
        memory.StringField(0x38000 + 112, "a-different-team");
        Assert.Null(memory.Read());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsTheActiveBattleSceneAndItsEntireRosterEvenWithoutAPriorLobby(bool sawLobby)
    {
        var memory = new Sample();
        memory.EnterLobby();
        if (sawLobby) Assert.Equal("00482", memory.Read()?.RoomId);
        else
        {
            memory.Pointer(0x33000 + 48, 0); // Direct reconnect has no active team.
            memory.Pointer(0x33000 + 80, 0);
        }
        memory.EnterBattle();
        var game = Assert.IsType<GameRoomObservation>(memory.Read());
        Assert.Equal(("battle-B", "playing"), (game.RoomId, game.Phase));
        Assert.Null(game.HostUid);
        Assert.Equal(["1001", "3003"], game.Members.Select(seat => seat.Uid));
        Assert.Equal(["博士甲", "官匹队友丙"], game.Members.Select(seat => seat.Nickname));
        memory.StringField(0x35000 + 160, "battle-C");
        Assert.Null(memory.Read());
        memory.StringField(0x36000 + 32, "battle-C");
        Assert.Equal("battle-C", memory.Read()?.RoomId);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 9)]
    public void AfterLeavingOrFinishingOnlyTheStoppedServiceYieldsIdle(bool didPlay, int finalTeamState)
    {
        var memory = new Sample();
        memory.EnterLobby();
        if (didPlay) memory.EnterBattle();
        Assert.Equal(didPlay ? "playing" : "lobby", memory.Read()?.Phase);
        memory.StopBattle(finalTeamState);
        Assert.Null(memory.Read()); // The old team and service instance are still allocated.
        memory.Pointer(0x30000, 0); // ServiceCoreBase<AutoChessService>.Stop clears s_instance.
        var idle = Assert.IsType<GameRoomObservation>(memory.Read());
        Assert.Equal(("idle", "1001", "博士甲"), (idle.Phase, idle.SelfUid, idle.Nickname));
        Assert.Null(idle.RoomId);
        Assert.Empty(idle.Members);
        memory.SoloQueue();
        Assert.Equal("matching", memory.Read()?.Phase);
    }

    [Fact]
    public void AnOfficiallyMatchedTeamWithoutAnOwnerStillReportsAllSeats()
    {
        var memory = new Sample();
        memory.EnterLobby();
        memory.Number(0x37000 + 24, 3);
        memory.StringField(0x37000 + 40, "");
        var game = Assert.IsType<GameRoomObservation>(memory.Read());
        Assert.Equal(("matching", "00482"), (game.Phase, game.RoomId));
        Assert.Null(game.HostUid);
        Assert.Equal(2, game.Members.Length);
        Assert.All(game.Members, seat => Assert.False(seat.Ready));
    }

    [Fact]
    public void AnEmptyOrDestroyedActivePageIsUnknownDuringNavigation()
    {
        var memory = new Sample();
        memory.Pointer(0x25000 + 16, 0);
        Assert.Null(memory.Read());
    }

    [Fact]
    public void GameSoloQueueHasAnIdentityAndNoRoomUntilAMatchArrives()
    {
        var memory = new Sample();
        memory.SoloQueue();
        var queue = Assert.IsType<GameRoomObservation>(memory.Read());
        Assert.Equal(("1001", "matching"), (queue.SelfUid, queue.Phase));
        Assert.Null(queue.RoomId);
        Assert.Empty(queue.Members);
        memory.Number(0x62000 + 20, 2);
        memory.Number(0x62000 + 24, 2); // Successful result is waiting for the game to join its server.
        Assert.Equal("matching", memory.Read()?.Phase);
        memory.Number(0x62000 + 24, 1); // Cancelled by the game.
        Assert.Equal("idle", memory.Read()?.Phase);
    }

    [Fact]
    public void AChangingRosterAMissingSelfOrAnUninitialisedScreenIsUnknown()
    {
        var memory = new Sample();
        memory.EnterLobby();
        memory.ChangeOnSecondRead = 0x37000 + 24;
        Assert.Null(memory.Read());
        memory.ChangeOnSecondRead = 0;
        memory.StringField(0x41000 + 16, "3003");
        Assert.Null(memory.Read());
        memory = new Sample();
        memory.Pointer(0x22000 + 16, 0);
        Assert.Null(memory.Read());
    }

    // The byte addresses below are fictitious. Field/array layouts are taken from the installed 2.7.91 x64 image.
    private sealed class Sample
    {
        private const string Game = "Torappu.UI.AutoChess", Server = Game + ".Server", Battle = "Torappu.Battle.AutoChess";
        private const int Module = 0x10000000;
        private readonly Dictionary<long, byte> _data = [];
        private readonly Dictionary<long, (string, string)> _types = [];
        private readonly Dictionary<long, string> _strings = [];
        private long _next = 0x100000;
        public long ChangeOnSecondRead;
        private int _seen;

        public Sample()
        {
            var sdkClass = Class("U8SDKInterface", "U8.SDK");
            var windowsSdkClass = Class("SDKInterfaceWIN", "U8.SDK");
            var playerClass = Class("PlayerData", "Torappu");
            var pagesClass = Class("UIPageController", "Torappu.UI");
            var singleton = Class("Singleton`1", "Torappu");
            var mono = Class("SingletonMonoBehaviour`1", "Torappu");
            Pointer(Module + 0x8b03820, sdkClass);
            Pointer(Module + 0x8b68e20, playerClass);
            Pointer(Module + 0x8b47130, pagesClass);
            Pointer(Module + 0x8c61c70, 0x20015cb7); // Untouched build-specific type reference.
            Pointer(sdkClass + 184, 0x12000); Pointer(0x12000, 0x13000); Pointer(0x13000, windowsSdkClass);
            Pointer(windowsSdkClass + 88, sdkClass);
            Pointer(playerClass + 88, singleton); Pointer(singleton + 184, 0x14000);
            Pointer(0x14000, 0x15000); Pointer(0x15000, playerClass);
            Obj(0x16000, "PlayerDataModel", "Torappu"); Pointer(0x15000 + 32, 0x16000);
            Obj(0x17000, "PlayerStatus", "Torappu"); Pointer(0x16000 + 32, 0x17000);
            StringField(0x13000 + 104, "1001"); StringField(0x17000 + 16, "博士甲");
            Pointer(pagesClass + 88, mono); Pointer(mono + 184, 0x21000);
            Pointer(0x21000, 0x22000); Pointer(0x22000, pagesClass); Pointer(0x22000 + 16, 0x90000);
            Byte(0x22000 + 120, 0); Pointer(0x22000 + 104, 0x23000); List(0x23000, 0x24000, 1);
            Pointer(0x24000 + 40, 0x25000); Obj(0x25000, "HomePage", "Torappu.UI"); Pointer(0x25000 + 16, 0xa0000);
        }

        public void EnterLobby()
        {
            var service = Class("AutoChessService", Server);
            var parent = Class("ServiceBase`2", "Torappu.SocketNetwork.SvrCom");
            var core = Class("ServiceCoreBase`1", "Torappu.SocketNetwork.SvrCom");
            Pointer(Module + 0x8c61c70, service); Pointer(service + 88, parent); Pointer(parent + 88, core);
            Pointer(core + 184, 0x30000); Pointer(0x30000, 0x31000); Pointer(0x31000, service);
            Pointer(0x31000 + 24, 0x32000); Obj(0x32000, "AutoChessServiceTeamPhase", Server);
            Pointer(0x31000 + 40, 0x33000); Obj(0x33000, "AutoChessServiceMode", Server);
            Pointer(0x33000 + 40, 0x35000); Obj(0x35000, "AutoChessBattleServer", Server);
            Pointer(0x35000 + 200, 0x36000); Obj(0x36000, "AutoChessServiceBattleInfo", Server);
            Byte(0x36000 + 16, 0); Pointer(0x33000 + 80, 0x37000); Obj(0x37000, "AutoChessServiceTeamInfo", Server);
            Pointer(0x33000 + 48, 0x38000); Obj(0x38000, "AutoChessTeamServer", Server);
            Number(0x37000 + 24, 1); StringField(0x37000 + 40, "2002");
            StringField(0x37000 + 144, "00482"); StringField(0x38000 + 112, "00482");
            Pointer(0x37000 + 64, 0x39000); List(0x39000, 0x3a000, 2);
            Pointer(0x3a000 + 32, 0x40000); Obj(0x40000, "MsgAutoChessPlayerStatus", Server);
            StringField(0x40000 + 16, "2002"); StringField(0x40000 + 24, "队友乙"); Number(0x40000 + 116, 0);
            Pointer(0x3a000 + 40, 0x41000); Obj(0x41000, "MsgAutoChessPlayerStatus", Server);
            StringField(0x41000 + 16, "1001"); StringField(0x41000 + 24, "博士甲"); Number(0x41000 + 116, 0);
        }

        public void EnterBattle()
        {
            Obj(0x32000, "AutoChessServiceBattlePhase", Server);
            Byte(0x36000 + 16, 1); StringField(0x35000 + 160, "battle-B"); StringField(0x36000 + 32, "battle-B");
            Pointer(0x36000 + 48, 0x45000); Obj(0x45000, "AutoChessBattleSceneData", Game);
            Pointer(0x45000 + 24, 0x46000); Obj(0x46000, "AutoChessScenePlayerStaticData", "");
            Pointer(0x46000 + 24, 0x47000); Obj(0x47000, "ScenePlayerStaticData", Battle);
            Pointer(0x47000 + 16, 0x48000); List(0x48000, 0x49000, 2, pairs: true);
            Number(0x49000 + 32, 0); Pointer(0x49000 + 40, 0x4a000); Obj(0x4a000, "StaticScenePlayerData", Battle);
            Number(0x4a000 + 16, 0); StringField(0x4a000 + 24, "1001"); Pointer(0x4a000 + 56, 0x4b000);
            Obj(0x4b000, "PlayerCard", Battle); StringField(0x4b000 + 16, "博士甲");
            Number(0x49000 + 48, 1); Pointer(0x49000 + 56, 0x4c000); Obj(0x4c000, "StaticScenePlayerData", Battle);
            Number(0x4c000 + 16, 1); StringField(0x4c000 + 24, "3003"); Pointer(0x4c000 + 56, 0x4d000);
            Obj(0x4d000, "PlayerCard", Battle); StringField(0x4d000 + 16, "官匹队友丙");
        }

        public void StopBattle(int teamState)
        {
            Obj(0x32000, "AutoChessServiceTeamPhase", Server);
            Byte(0x36000 + 16, 0);
            Number(0x37000 + 24, teamState);
        }

        public void SetSdkWithWrongBase()
        {
            long impostor = Class("SDKInterfaceWIN", "U8.SDK");
            Pointer(impostor + 88, Class("OtherInterface", "U8.SDK"));
            Pointer(0x13000, impostor);
        }

        public void SoloQueue()
        {
            Obj(0x25000, "AutoChessPreparePage", Game); Pointer(0x25000 + 256, 0x57000);
            Obj(0x57000, "UICompDialogMgr", "Torappu.UI"); Pointer(0x57000 + 16, 0x58000);
            List(0x58000, 0x59000, 1, pairs: true); Number(0x59000 + 32, 42); Pointer(0x59000 + 40, 0x60000);
            Obj(0x60000, "DialogWrapper", ""); Byte(0x60000 + 16, 0); Byte(0x60000 + 17, 0);
            Pointer(0x60000 + 32, 0x61000); Obj(0x61000, "AutoChessSoloMatchingDialog", Game);
            Byte(0x61000 + 48, 0); Number(0x61000 + 80, 42); Pointer(0x61000 + 152, 0x62000);
            Obj(0x62000, "AutoChessMultiMatchViewModel", Game); Number(0x62000 + 20, 1); Number(0x62000 + 24, 0);
        }

        public GameRoomObservation? Read()
        {
            _seen = 0;
            return new GameRoomMemoryReader(Module, Bytes, (p, n, ns) => _types.GetValueOrDefault(p) == (n, ns),
                p => _strings[p]).Read();
        }

        private long Class(string name, string ns) { var p = _next += 0x200; _types.Add(p, (name, ns)); return p; }
        private void Obj(long p, string name, string ns) => Pointer(p, Class(name, ns));
        private void Byte(long address, byte value) => _data[address] = value;
        public void Number(long p, int i) => Write(p, BitConverter.GetBytes(i));
        public void Pointer(long p, long i) => Write(p, BitConverter.GetBytes(i));
        public void StringField(long field, string value)
        {
            var address = _next += 0x200;
            _strings.Add(address, value);
            Pointer(field, address);
        }
        private void Write(long p, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) _data[p + i] = bytes[i];
        }
        private byte[] Bytes(long address, int length)
        {
            var bytes = new byte[length];
            for (int i = 0; i < length; i++) bytes[i] = _data.GetValueOrDefault(address + i);
            if (address == ChangeOnSecondRead && ++_seen == 2) bytes[0]++;
            return bytes;
        }
        private void List(long list, long array, int count, bool pairs = false)
        {
            Obj(list, pairs ? "ListDict`2" : "List`1", pairs ? "Torappu" : "System.Collections.Generic");
            Pointer(list + 16, array); Number(list + 24, count); Pointer(array + 24, count);
        }
    }
}
