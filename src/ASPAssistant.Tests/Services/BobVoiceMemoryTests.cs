using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public sealed class BobVoiceMemoryTests
{
    private readonly BobVoiceTriggers _triggers = new();
    private static readonly GarrisonVoiceFrame Shop = new(Preparation: true, ShopVisible: true);

    [Fact]
    public void FreezeRequiresAConfirmedTransitionAndDoesNotRepeatOnAttachOrThaw()
    {
        var baseline = Snapshot();
        Confirm(baseline, null);
        var frozen = baseline with { Shop = baseline.Shop! with { Offers = [new("chess_char_1_01_a", 0, 2, true)] } };
        Assert.Null(Observe(frozen));
        Assert.Equal(BobVoiceCue.ShopFrozen, Observe(frozen));
        Assert.Null(Observe(frozen));
        Confirm(baseline, null);
        _triggers.Reset();
        Confirm(frozen, null);
    }

    [Fact]
    public void PaidAndFreeRefreshesUseOffersAndRefreshCounters()
    {
        var first = Snapshot() with { Shop = Snapshot().Shop! with { RefreshCost = 2 } };
        Confirm(first, null);
        var paid = first with { Shop = first.Shop! with { Coins = 8, Offers = [new("chess_char_1_02_a", 0, 3, false)] } };
        Confirm(paid, BobVoiceCue.ShopRefreshed);
        var free = paid with { Shop = paid.Shop! with { FreeRefreshes = 1 } };
        Confirm(free, null);
        Confirm(free with { Shop = free.Shop! with { FreeRefreshes = 0,
            Offers = [new("chess_char_1_03_a", 0, 3, false)] } }, BobVoiceCue.ShopRefreshed);
    }

    [Fact]
    public void TwoCoinPurchaseAndSaleUseInventoryRatherThanGoldHeuristics()
    {
        var first = Snapshot();
        Confirm(first, null);
        var bought = first with { Shop = first.Shop! with { Coins = 8, Offers = [] },
            Characters = [new(10, "chess_char_1_01_a", [], 1)] };
        Confirm(bought, BobVoiceCue.Recruit);
        Confirm(bought with { Shop = bought.Shop! with { Coins = 9 }, Characters = [] }, BobVoiceCue.Sell);
    }

    [Fact]
    public void IncomeAndSpendingWithoutInventoryChangesDoNotPretendToBeTransactions()
    {
        var first = Snapshot();
        Confirm(first, null);
        var spent = first with { Shop = first.Shop! with { Coins = 6 } };
        Confirm(spent, null);
        Confirm(spent with { Shop = spent.Shop! with { Coins = 7 } }, null);
    }

    [Fact]
    public void BuyingEquipmentDoesNotLookLikeARefresh()
    {
        var first = Snapshot();
        Confirm(first, null);
        Confirm(first with { Shop = first.Shop! with { Coins = 9, Offers = [] },
            Equipment = [new(20, "chess_equip_01_a")] }, null);
    }

    [Fact]
    public void AReplenishedEquipmentPurchaseDoesNotLookLikeAPaidRefresh()
    {
        var first = Snapshot() with { Shop = Snapshot().Shop! with {
            Offers = [new("chess_equip_01_a", 0, 1, false)] } };
        Confirm(first, null);
        Confirm(first with { Shop = first.Shop! with { Coins = 9,
            Offers = [new("chess_equip_02_a", 0, 1, false)] },
            Equipment = [new(20, "chess_equip_01_a")] }, null);
    }

    [Fact]
    public void GiftedCharactersAndUnrelatedSpendingDoNotLookLikeARecruit()
    {
        var first = Snapshot();
        Confirm(first, null);
        Confirm(first with { Shop = first.Shop! with { Coins = 6 },
            Characters = [new(10, "chess_char_1_03_a", [], 1)] }, null);
    }

    [Fact]
    public void OneFrameNoiseAndUnknownInventoryCannotInventRecruitment()
    {
        var first = Snapshot();
        Confirm(first, null);
        var changed = first with { Shop = first.Shop! with { Coins = 8, Offers = [] },
            Characters = [new(10, "chess_char_1_01_a", [], 1)] };
        Assert.Null(Observe(changed));
        Assert.Null(Observe(first));
        Assert.Null(Observe(first));
        Confirm(first with { Characters = null }, null);
        Confirm(changed, null);
    }

    [Fact]
    public void EliteReplacementUsesThePromotionCueAndIgnoresEquipmentChanges()
    {
        var first = Snapshot() with { Characters = [new(10, "chess_char_1_01_a", [], 1)] };
        Confirm(first, null);
        var equipped = first with { Characters = [new(10, "chess_char_1_01_a", [42], 1)] };
        Confirm(equipped, null);
        Confirm(equipped with { Characters = [new(10, "chess_char_1_01_b", [42], 3)] }, BobVoiceCue.Promotion);
    }

    [Fact]
    public void MergeWithNewInstanceTakesPriorityOverRecruitmentAndSale()
    {
        var first = Snapshot() with { Characters = [
            new(10, "chess_char_1_01_a", [], 1), new(11, "chess_char_1_01_a", [], 1)] };
        Confirm(first, null);
        Confirm(first with { Shop = first.Shop! with { Coins = 8, Offers = [] },
            Characters = [new(12, "chess_char_1_01_b", [], 3)] }, BobVoiceCue.Promotion);
    }

    [Fact]
    public void InterruptedSourceOrHiddenShopReestablishesTheBaseline()
    {
        var first = Snapshot();
        Confirm(first, null);
        Assert.Null(_triggers.Observe(new(4, 2, 10), [], Shop, memory:
            new(BattleFeedState.ReadInterrupted, GarrisonInterrupted: true)));
        var frozen = first with { Shop = first.Shop! with { Offers = [new("chess_char_1_01_a", 0, 2, true)] } };
        Confirm(frozen, null);
        Observe(frozen, new(ShopVisible: false));
        Confirm(first, null);
    }

    [Fact]
    public void RoundPlayerAndActivityChangesCannotLookLikeAShopAction()
    {
        var first = Snapshot();
        Confirm(first, null);
        var next = first with { Round = 5, Shop = first.Shop! with { Offers = [new("chess_char_1_01_a", 0, 2, true)] } };
        Confirm(next, null);
        Confirm(next with { PlayerIndex = 1 }, null);
        Confirm(next with { ActivityId = "act4autochess" }, null);
    }

    [Theory]
    [InlineData(GameTargetKind.Windows)]
    [InlineData(GameTargetKind.MuMu)]
    public void EarlyBattleFrameCanAnnounceBattleStartWithoutRepeating(GameTargetKind source)
    {
        var frame = new BattleFrame(8, 15, .5, false, []);
        var update = new BattleUpdate(BattleFeedState.Live, frame) { Source = source };
        Assert.Equal(BobVoiceCue.BattleStart, _triggers.Observe(new(4, null, null), [], memory: update));
        Assert.Null(_triggers.Observe(new(4, null, null), [], memory: update));
        update = update with { Sample = frame with { Round = 9, GameSeconds = 20 } };
        Assert.Null(_triggers.Observe(new(5, null, null), [], memory: update));
    }

    [Fact]
    public void BattleFramesOutsideGarrisonOrAfterAPreBattleReminderStaySilent()
    {
        var update = new BattleUpdate(BattleFeedState.Live, new(8, 15, .5, false, []));
        Assert.Null(_triggers.Observe(default, [], memory: update));
        Assert.Equal(BobVoiceCue.BeforeBattle, _triggers.Observe(new(4, null, null), [],
            new(BattleCue: BobVoiceCue.BeforeBattle)));
        Assert.Null(_triggers.Observe(new(4, null, null), [], memory: update));
    }

    [Fact]
    public void AnEarlyClockWhileTheShopIsVisibleWaitsUntilCombat()
    {
        var update = new BattleUpdate(BattleFeedState.Live, new(8, 15, .5, false, []));
        Assert.Null(_triggers.Observe(new(4, null, null), [], Shop, memory: update));
        Assert.Equal(BobVoiceCue.BattleStart, _triggers.Observe(new(4, null, null), [], memory: update));
    }

    [Fact]
    public void EndedAndInterruptedBattleFramesCannotAnnounceAStart()
    {
        var frame = new BattleFrame(8, 15, .5, true, []);
        Assert.Null(_triggers.Observe(new(4, null, null), [],
            memory: new(BattleFeedState.Live, frame)));
        Assert.Null(_triggers.Observe(new(4, null, null), [],
            memory: new(BattleFeedState.ReadInterrupted, frame with { Round = 9, Ended = false })));
    }

    [Fact]
    public void PcMemoryProvidesLowHealthForThemesWithoutAHealthRecognitionRegion()
    {
        var low = Snapshot() with { Health = 8 };
        Assert.Null(Observe(low, default));
        Assert.Equal(BobVoiceCue.LowHealth, Observe(low, default));
    }

    private BobVoiceCue? Observe(GarrisonSnapshot snapshot, GarrisonVoiceFrame? voice = null) =>
        _triggers.Observe(GarrisonHud.FromMemory(snapshot), [], voice ?? Shop,
            memory: new(BattleFeedState.WaitingForRound, Garrison: snapshot));

    private void Confirm(GarrisonSnapshot snapshot, BobVoiceCue? expected)
    {
        Assert.Null(Observe(snapshot));
        Assert.Equal(expected, Observe(snapshot));
    }

    private static GarrisonSnapshot Snapshot() => new(4, 0, 30, 2, 10,
        new(2, 10, 4, 1, 0, [new("chess_char_1_01_a", 0, 2, false)]), [], [], [], [],
        "act3autochess");
}
