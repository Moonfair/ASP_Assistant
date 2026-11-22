using System.Text.Json;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public class GarrisonMemoryReaderTests
{
    // Two players, with the local player second in the list. Numeric offsets are from the PC 2.7.91 layout.
    private sealed class Sample
    {
        private const string Chess = "Torappu.Battle.AutoChess", Generic = "System.Collections.Generic";
        private readonly byte[] _data = new byte[0x10000];
        private readonly Dictionary<long, (string, string)> _types = new()
        {
            [0x2000] = ("AutoChessGameMode", ""), [0x2800] = ("SingletonHost", ""),
            [0x3000] = ("Dictionary`2", Generic), [0x4000] = ("AutoChessGameManager", Chess),
            [0x4800] = ("AutoChessDataCenter", "Torappu.Battle.DataCenter"), [0x5000] = ("AutoChessPlayerDataModel", Chess),
            [0x5800] = ("ListDict`2", "Torappu"), [0x6800] = ("ScenePlayerData", ""), [0x6400] = ("ScenePlayerData", ""),
            [0x7000] = ("PlayerBattleData", Chess), [0x7800] = ("List`1", Generic),
            [0x8000] = ("List`1", Generic), [0x9000] = ("EquipChess", Chess),
            [0xA000] = ("AutoChessShopDataModel", Chess), [0xA200] = ("List`1", Generic),
            [0xA600] = ("CharChess", Chess), [0xA800] = ("List`1", Generic),
            [0xAE00] = ("List`1", Generic), [0xB200] = ("ChessPositionInfo", Chess),
        };
        public GarrisonMemoryReader Reader { get; }
        public bool ChangeCoin;
        public void Ptr(int address, long value) => BitConverter.GetBytes(value).CopyTo(_data, address);
        public void Num(int address, int value) => BitConverter.GetBytes(value).CopyTo(_data, address);
        public Sample()
        {
            Ptr(0x1000 + 16, 0x12340); Ptr(0x1000 + 376, 0x2000); Ptr(0x1000 + 744, 0x2800);
            Ptr(0x2800 + 16, 0x3000); Ptr(0x3000 + 24, 0x3800); Num(0x3000 + 32, 1);
            Ptr(0x3800 + 24, 3); Ptr(0x3800 + 40, 0x3210); Ptr(0x3800 + 48, 0x4000);
            Ptr(0x4000 + 64, 0x4800); Num(0x4800 + 56, 2); Num(0x4800 + 60, 12); Ptr(0x4800 + 248, 0x5000);
            Ptr(0x5000 + 24, 0x5800); Ptr(0x5800 + 16, 0x6000); Num(0x5800 + 24, 2);
            Ptr(0x6000 + 24, 4); Num(0x6000 + 32, 1); Ptr(0x6000 + 40, 0x6400);
            Num(0x6400 + 16, 1); Num(0x6400 + 80, 17); Num(0x6400 + 88, 3); Num(0x6400 + 104, 4);
            Num(0x6000 + 48, 2); Ptr(0x6000 + 56, 0x6800);
            Num(0x6800 + 16, 2); Num(0x6800 + 88, 4); Num(0x6800 + 92, 0); Ptr(0x6800 + 128, 0x7000);
            Ptr(0x7000 + 16, 0x7800); Ptr(0x7000 + 24, 0x8000);
            Ptr(0x8000 + 16, 0x8800); Num(0x8000 + 24, 1); Ptr(0x8800 + 24, 1); Ptr(0x8800 + 32, 0x9000);
            Num(0x9000 + 16, 42); Ptr(0x9000 + 24, 0x9800);
            Reader = new(Read, (p, name, ns) => _types.GetValueOrDefault(p) == (name, ns),
                p => p switch { 0x9800 => "chess_item", 0xB400 => "chess_char", _ => throw new InvalidDataException() });
        }
        public void AddShopAndCharacter()
        {
            Ptr(0x4800 + 256, 0xA000); Num(0xA000 + 24, 5); Num(0xA000 + 28, 11);
            Ptr(0xA000 + 56, 0xA200);
            List(0xA200, 0xA400, 0); // One vacant shop slot.
            List(0x7800, 0xAC00, 0xA600);
            Num(0xA600 + 16, 11); Ptr(0xA600 + 24, 0xA800); Ptr(0xA600 + 32, 0xB400); Num(0xA600 + 44, 3);
            List(0xA800, 0xAA00, 42);
            Ptr(0x7000 + 40, 0xAE00); List(0xAE00, 0xB000, 0xB200);
            Num(0xB200 + 16, 11); Num(0xB200 + 20, -2);
        }
        public void AddOverview()
        {
            const string server = "Torappu.UI.AutoChess.Server";
            _types[0xB800] = ("AutoChessQuickViewDataModel", Chess);
            _types[0xBA00] = ("ListDict`2", "Torappu");
            _types[0xBE00] = ("AutoChessBattleQuickViewContent", server);
            _types[0xC400] = ("AutoChessBattleCharChess", server);
            _types[0xCA00] = ("AutoChessBattleEquipOrTrapChess", server);
            _types[0xD400] = ("AutoChessBattleChessBondInfo", server);
            _types[0xD600] = ("AutoChessDataIndexer", "Torappu.Battle.DataCenter");
            _types[0xD800] = ("Dictionary`2", Generic);
            foreach (int p in new[] { 0xC000, 0xC600, 0xCC00, 0xD000 }) _types[p] = ("List`1", Generic);
            Ptr(0x4800 + 288, 0xB800); Ptr(0xB800 + 24, 0xBA00);
            List(0xBA00, 0xBC00, 1); Ptr(0xBC00 + 40, 0xBE00); Num(0xBE00 + 16, 1);
            Ptr(0xBE00 + 24, 0xC000); List(0xC000, 0xC200, 0xC400);
            Num(0xC400 + 16, 5); Num(0xC400 + 32, 111); Ptr(0xC400 + 24, 0xCC00); List(0xCC00, 0xCE00, 7);
            Ptr(0xBE00 + 32, 0xC600); List(0xC600, 0xC800, 0xCA00); Num(0xCA00 + 16, 7); Num(0xCA00 + 20, 222);
            Ptr(0xBE00 + 40, 0xD000); List(0xD000, 0xD200, 0xD400); Num(0xD400 + 16, 77); Num(0xD400 + 28, 3);
            Ptr(0x4800 + 296, 0xD600); Ptr(0xD600 + 48, 0xD800); Ptr(0xD800 + 24, 0xDA00); Num(0xD800 + 32, 2);
            Ptr(0xDA00 + 24, 3); Num(0xDA00 + 40, 111); Ptr(0xDA00 + 48, 0xB400);
            Num(0xDA00 + 64, 222); Ptr(0xDA00 + 72, 0x9800);
        }
        private void List(int list, int array, long element)
        {
            Ptr(list + 16, array); Num(list + 24, 1); Ptr(array + 24, 1); Ptr(array + 32, element);
        }
        private byte[] Read(long p, int length)
        {
            var bytes = _data.AsSpan((int)p, length).ToArray();
            if (ChangeCoin && p == 0x6800 + 92) Num((int)p, 7);
            return bytes;
        }
        public GarrisonSnapshot? Capture() => Reader.Read(() => (0x800, 0x1000));
    }

    [Fact]
    public void PublishesTheOtherPlayersStateIndependentlyOfTheLocalInventory()
    {
        var memory = new Sample();
        memory.AddOverview();
        var json = JsonSerializer.Serialize(new BattleUpdate(BattleFeedState.WaitingForRound, Garrison: memory.Capture()));
        var teammate = Assert.Single(JsonSerializer.Deserialize<BattleUpdate>(json)!.Garrison!.Teammates!);
        Assert.Equal(1, teammate.PlayerIndex);
        Assert.Equal(17, teammate.Health);
        Assert.Equal(0, teammate.RawCoins);
        Assert.Equal(4, teammate.Streak);
        Assert.Null(teammate.Roster);
        var overview = teammate.Overview!;
        Assert.Null(overview.Positions);
        var character = Assert.Single(overview.Characters!);
        Assert.Equal("chess_char", character.ChessId);
        Assert.Equal(new GarrisonItem(Assert.Single(character.EquipmentSlots!), "chess_item"), Assert.Single(overview.Equipment!));
        Assert.Equal("编号 77", Assert.Single(overview.Bonds!).Id);
        memory.Num(0xBE00 + 16, 2); // Overview still points at a different player's cache during a refresh.
        Assert.Null(Assert.Single(memory.Capture()!.Teammates!).Overview);
        memory.Num(0x6000 + 48, 1); memory.Ptr(0x6000 + 56, 0x6400);
        memory.Num(0x5800 + 24, 1); memory.Num(0x4800 + 56, 1);
        Assert.Empty(memory.Capture()!.Teammates!);
    }

    [Fact]
    public void PreservesWearerInstanceAndPositionWhenAShopSlotIsVacant()
    {
        var memory = new Sample();
        memory.AddShopAndCharacter();
        var state = memory.Capture()!;
        Assert.Equal(5, state.Shop!.Level);
        Assert.Equal(11, state.Shop.Coins);
        Assert.Empty(state.Shop.Offers!);
        var character = Assert.Single(state.Characters!);
        Assert.Equal("chess_char", character.ChessId);
        Assert.Equal(Assert.Single(state.Equipment!).InstanceId, Assert.Single(character.EquipmentSlots!));
        Assert.Equal(new GarrisonPosition(character.InstanceId, -2, 0, 0), Assert.Single(state.Positions!));
    }

    [Fact]
    public void LocalInventoryPreservesEmptyAndUnknownAcrossTheWorkerPipe()
    {
        var sample = new Sample().Capture();
        var json = JsonSerializer.Serialize(new BattleUpdate(BattleFeedState.WaitingForRound, Garrison: sample));
        var decoded = Assert.IsType<GarrisonSnapshot>(JsonSerializer.Deserialize<BattleUpdate>(json)!.Garrison);
        Assert.Equal(12, decoded.Round);
        Assert.Equal(4, decoded.PlayerLevel);
        Assert.Equal(0, decoded.PlayerCoins);
        Assert.Empty(decoded.Characters!);
        Assert.Equal(new GarrisonItem(42, "chess_item"), Assert.Single(decoded.Equipment!));
        Assert.Null(decoded.Positions);
    }

    [Fact]
    public void DetectsInPlacePurchaseAndRecoversOnTheNextPoll()
    {
        var sample = new Sample { ChangeCoin = true };
        Assert.Throws<InvalidDataException>(() => sample.Capture());
        Assert.Equal(7, sample.Capture()!.PlayerCoins);
        sample.Ptr(0x1000 + 376, 0);
        Assert.Null(sample.Capture());
    }
}
