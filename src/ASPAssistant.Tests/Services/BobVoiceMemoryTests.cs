using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public sealed class BobVoiceMemoryTests
{
    private readonly BobVoiceTriggers _triggers = new();
    private static readonly GarrisonVoiceFrame Shop = new(Preparation: true, ShopVisible: true);

    [Fact]
    public void DraftRetriesDuringTheDecisionButNeverUsesScreenOrStaleMemoryAsAFallback()
    {
        var draft = Snapshot() with { Phase = new(2, 3, "enemy_initial_1") };
        var update = new BattleUpdate(BattleFeedState.WaitingForRound, Garrison: draft);
        var hud = GarrisonHud.FromMemory(draft);
        Assert.Null(_triggers.Observe(hud, [], Shop)); // A preparation screen alone is not a decision.
        Assert.Null(_triggers.Observe(hud, [], Shop, memory: update with { Source = GameTargetKind.MuMu }));
        Assert.Null(_triggers.Observe(hud, [], Shop, memory: update with { GarrisonInterrupted = true }));
        Assert.Null(_triggers.Observe(hud with { Round = 5 }, [], Shop, memory: update));
        Assert.Equal(BobVoiceCue.DraftChoice, _triggers.Observe(hud, [], default, memory: update));
        _triggers.RetryDraftChoice();
        Assert.Equal(BobVoiceCue.DraftChoice, _triggers.Observe(hud, [], default, memory: update));
        Assert.Null(_triggers.Observe(hud, [], Shop, memory: update));
        _triggers.RetryDraftChoice();
        Assert.Null(Observe(draft with { Phase = new(3, 3, null) }));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(1, 3)]
    public void ThreeOrMoreNormalCopiesGiveOneTripleReminder(int owned, int offered)
    {
        const string id = "chess_char_1_01_a";
        var first = Snapshot();
        var triple = first with {
            Characters = Enumerable.Range(1, owned).Select(i => new GarrisonCharacter(i, id, [], 1)).ToArray(),
            Positions = Enumerable.Range(1, owned).Select(i => new GarrisonPosition(i, i == 1 ? 65537 : -i, 0, 0)).ToArray(),
            Shop = first.Shop! with { Offers = Enumerable.Range(0, offered)
                .Select(i => new GarrisonOffer(id, i, 2, false)).ToArray() }
        };
        Assert.Null(Observe(triple));
        Assert.Equal(BobVoiceCue.TripleOpportunity, Observe(triple));
        Assert.Null(Observe(triple));
    }

    [Fact]
    public void EliteCopiesOtherNamesEquipmentAndEmptySlotsCannotCompleteATriple()
    {
        var first = Snapshot() with { Characters = [new(1, "chess_char_1_01_a", [], 1),
            new(2, "chess_char_1_01_b", [], 3), new(3, "chess_char_1_02_a", [], 1)] };
        Confirm(first with { Shop = first.Shop! with { Offers = [new("chess_char_1_01_a", 0, 2, true),
            new("chess_char_1_01_b", 1, 2, false), new("", 2, 0, false),
            new("chess_equip_01_a", 3, 1, false), new("chess_equip_01_a", 4, 1, false),
            new("chess_equip_01_a", 5, 1, false)] } }, null);
    }

    [Fact]
    public void TripleReminderWinsOverRefreshAndOnlyRearmsAfterAConfirmedDrop()
    {
        const string id = "chess_char_1_01_a";
        var pair = Snapshot() with { Characters = [new(1, id, [], 1), new(2, id, [], 1)],
            Shop = Snapshot().Shop! with { Offers = [new("chess_char_1_02_a", 0, 2, false)] } };
        var triple = pair with { Shop = pair.Shop! with { Coins = 9, Offers = [new(id, 0, 2, true)] } };
        Confirm(pair, null);
        Confirm(triple, BobVoiceCue.TripleOpportunity);
        Assert.Null(Observe(pair)); // One noisy frame does not rearm the same reminder.
        Assert.Null(Observe(triple));
        Assert.Null(Observe(triple));
        Confirm(pair, null);
        Confirm(triple, BobVoiceCue.TripleOpportunity);
    }

    [Fact]
    public void CooldownRetriesOnlyWhileTheTripleStillExists()
    {
        const string id = "chess_char_1_01_a";
        var triple = Snapshot() with { Characters = [new(1, id, [], 1), new(2, id, [], 1)] };
        Confirm(triple, BobVoiceCue.TripleOpportunity);
        _triggers.RetryTripleOpportunity();
        Assert.Equal(BobVoiceCue.TripleOpportunity, Observe(triple));
        _triggers.RetryTripleOpportunity();
        Confirm(triple with { Shop = triple.Shop! with { Offers = [] } }, null);
    }

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
        Confirm(first, BobVoiceCue.TripleOpportunity);
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
