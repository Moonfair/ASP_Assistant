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

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(120)]
    public void ChatterWaitsWithinTheConfiguredRandomInterval(int interval)
    {
        var triggers = new BobVoiceTriggers { ChatterIntervalSeconds = interval };
        var hud = new GarrisonHud(5, null, null);

        Assert.Null(Observe(0));
        Assert.Null(Observe(interval - 1));
        Assert.Equal(BobVoiceCue.PreparationChatter, Observe(interval * 2));
        Assert.Null(Observe(interval * 3 - 1));
        Assert.Equal(BobVoiceCue.PreparationChatter, Observe(interval * 4));

        triggers.Reset();
        Assert.Null(Observe(0));
        Assert.Null(Observe(interval - 1));
        Assert.Equal(BobVoiceCue.PreparationChatter, Observe(interval * 2));

        BobVoiceCue? Observe(int seconds) =>
            triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void ChangingChatterIntervalReplacesPendingWaitWithoutRepeatingEventCues()
    {
        var triggers = new BobVoiceTriggers();
        var hud = new GarrisonHud(5, null, null);

        Assert.Null(Observe(0));
        triggers.ChatterIntervalSeconds = 60;
        Assert.Null(Observe(1));
        Assert.Null(Observe(40));
        triggers.ChatterIntervalSeconds = 10;
        Assert.Null(Observe(41));
        Assert.Equal(BobVoiceCue.PreparationChatter, Observe(61));

        var battle = new GarrisonVoiceFrame(BattleCue: BobVoiceCue.BeforeBattle);
        Assert.Equal(BobVoiceCue.BeforeBattle, triggers.Observe(hud, [], battle));
        triggers.ChatterIntervalSeconds = 120;
        Assert.Null(triggers.Observe(hud, [], battle));
        Assert.Null(Observe(1000));

        BobVoiceCue? Observe(int seconds) =>
            triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void ApplyingTheSameChatterIntervalDoesNotPostponeChatter()
    {
        var triggers = new BobVoiceTriggers();
        var hud = new GarrisonHud(5, null, null);
        Assert.Null(triggers.Observe(hud, [], new(Preparation: true), TimeSpan.Zero));

        triggers.ChatterIntervalSeconds = 15;

        Assert.Equal(BobVoiceCue.PreparationChatter,
            triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(30)));
    }
}
