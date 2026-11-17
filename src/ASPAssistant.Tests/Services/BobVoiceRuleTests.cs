using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public sealed class BobVoiceRuleTests
{
    [Fact]
    public void CombatCuesDeduplicateAndFirstVictoryOnlyPlaysOncePerMatch()
    {
        var triggers = new BobVoiceTriggers();
        var round = new GarrisonHud(5, null, null);
        Assert.Equal(BobVoiceCue.BeforeBattle, Battle(BobVoiceCue.BeforeBattle));
        Assert.Null(Battle(BobVoiceCue.BeforeBattle));
        Assert.Equal(BobVoiceCue.BattleLoss, Battle(BobVoiceCue.BattleLoss));
        Assert.Null(Battle(BobVoiceCue.BattleLoss));
        round = new GarrisonHud(6, null, null);
        Assert.Equal(BobVoiceCue.BattleVictory, Battle(BobVoiceCue.BattleVictory));
        round = new GarrisonHud(7, null, null);
        Assert.Null(Battle(BobVoiceCue.BattleVictory));
        triggers.Reset();
        Assert.Equal(BobVoiceCue.BattleVictory, Battle(BobVoiceCue.BattleVictory));

        BobVoiceCue? Battle(BobVoiceCue cue) => triggers.Observe(round, [], new(BattleCue: cue));
    }

    [Fact]
    public void LowHealthIsConfirmedAndDoesNotRepeatUntilRecovery()
    {
        var triggers = new BobVoiceTriggers();
        Assert.Null(Health(11));
        Assert.Null(Health(10));
        Assert.Null(Health(11));
        Assert.Null(Health(10));
        Assert.Equal(BobVoiceCue.LowHealth, Health(10));
        Assert.Null(Health(9));
        Assert.Null(Health(9));

        BobVoiceCue? Health(int hp) => triggers.Observe(new(4, null, null), [], new(Health: hp));
    }

    [Fact]
    public void DetectsConfirmedCoinChangesAndExcludesUpgradingAndCrossRoundIncome()
    {
        var triggers = new BobVoiceTriggers();
        Confirm(4, 2, 10, null);
        Confirm(4, 2, 8, null);
        Confirm(4, 2, 5, BobVoiceCue.Recruit);
        Confirm(4, 2, 6, BobVoiceCue.Sell);
        Confirm(4, 3, 3, BobVoiceCue.Upgrade);
        Confirm(5, 3, 4, null);
        Confirm(5, 3, 6, null);

        void Confirm(int round, int level, int gold, BobVoiceCue? expected)
        {
            var hud = new GarrisonHud(round, level, gold);
            Assert.Null(triggers.Observe(hud, []));
            Assert.Equal(expected, triggers.Observe(hud, []));
        }
    }

    [Fact]
    public void RestChatterWaitsForPreparationAndYieldsToCombat()
    {
        var triggers = new BobVoiceTriggers();
        var round = new GarrisonHud(5, null, null);
        var t = TimeSpan.FromSeconds;
        Assert.Null(triggers.Observe(round, [], new(Preparation: true), t(0)));
        Assert.Null(triggers.Observe(round, [], new(Preparation: true), t(10)));
        Assert.Equal(BobVoiceCue.PreparationChatter, triggers.Observe(round, [], new(Preparation: true), t(35)));
        Assert.Null(triggers.Observe(round, [], new(Preparation: true), t(36)));
        Assert.Equal(BobVoiceCue.BeforeBattle, triggers.Observe(round, [], new(Preparation: true, BattleCue: BobVoiceCue.BeforeBattle), t(70)));
        Assert.Null(triggers.Observe(round, [], new(Preparation: true), t(100)));
    }
}
