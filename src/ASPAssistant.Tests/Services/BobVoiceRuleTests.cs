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
        Assert.Null(triggers.Observe(round, [], default, t(0)));
        Assert.Null(triggers.Observe(round, [], new(Preparation: true), t(1)));
        Assert.Null(triggers.Observe(round, [], new(Preparation: true), t(5)));
        Assert.Equal(BobVoiceCue.PreparationChatter, triggers.Observe(round, [], new(Preparation: true), t(11)));
        Assert.Null(triggers.Observe(round, [], new(Preparation: true), t(12)));
        Assert.Equal(BobVoiceCue.BeforeBattle, triggers.Observe(round, [], new(Preparation: true, BattleCue: BobVoiceCue.BeforeBattle), t(20)));
        Assert.Null(triggers.Observe(round, [], new(Preparation: true), t(100)));
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 10)]
    public void ChatterWaitsFiveToTenSecondsBetweenOpportunities(bool maximumDelay, int delay)
    {
        var triggers = new BobVoiceTriggers(new ChatterRandom(0, 0, 0) { UseMaximumDelay = maximumDelay });
        var hud = new GarrisonHud(5, null, null);

        Assert.Null(Observe(0));
        Assert.Null(Observe(delay - 1));
        Assert.Equal(BobVoiceCue.PreparationChatter, Observe(delay));
        Assert.Null(Observe(delay * 2 - 1));
        Assert.Equal(BobVoiceCue.PreparationChatter, Observe(delay * 2));

        triggers.Reset();
        Assert.Null(Observe(0));
        Assert.Null(Observe(delay - 1));
        Assert.Equal(BobVoiceCue.PreparationChatter, Observe(delay));

        BobVoiceCue? Observe(int seconds) =>
            triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void SkippingAProbabilityCheckStillWaitsForTheNextOpportunity()
    {
        var triggers = new BobVoiceTriggers(new ChatterRandom(0.75, 0.25)) { ChatterFrequency = 0.5 };
        var hud = new GarrisonHud(5, null, null);

        Assert.Null(Observe(0));
        Assert.Null(Observe(5));
        for (int seconds = 6; seconds < 10; seconds++) Assert.Null(Observe(seconds));
        Assert.Equal(BobVoiceCue.PreparationChatter, Observe(10));

        BobVoiceCue? Observe(int seconds) =>
            triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void ZeroFrequencyDisablesOnlyRandomChatterAndSurvivesMatchReset()
    {
        var triggers = new BobVoiceTriggers { ChatterFrequency = 0 };
        var hud = new GarrisonHud(5, null, null);
        for (int seconds = 0; seconds <= 100; seconds++)
            Assert.Null(triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(seconds)));

        Assert.Equal(BobVoiceCue.BeforeBattle, triggers.Observe(hud, [], new(BattleCue: BobVoiceCue.BeforeBattle)));
        triggers.Reset();
        Assert.Null(triggers.Observe(hud, [], new(Preparation: true), TimeSpan.Zero));
        Assert.Null(triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(10)));
        Assert.Null(triggers.Observe(new(1, 1, 4), []));
        Assert.Equal(BobVoiceCue.Opening, triggers.Observe(new(1, 1, 4), []));
    }

    [Fact]
    public void ChangingFrequencyAppliesToThePendingOpportunityWithoutResettingEventCues()
    {
        var triggers = new BobVoiceTriggers(new ChatterRandom(0.9, 0.9)) { ChatterFrequency = 0 };
        var hud = new GarrisonHud(5, null, null);
        Assert.Null(triggers.Observe(hud, [], new(Preparation: true), TimeSpan.Zero));
        triggers.ChatterFrequency = 1;
        Assert.Equal(BobVoiceCue.PreparationChatter,
            triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(5)));
        triggers.ChatterFrequency = 1;
        Assert.Equal(BobVoiceCue.PreparationChatter,
            triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(10)));

        var battle = new GarrisonVoiceFrame(BattleCue: BobVoiceCue.BeforeBattle);
        Assert.Equal(BobVoiceCue.BeforeBattle, triggers.Observe(hud, [], battle));
        triggers.ChatterFrequency = 0.5;
        Assert.Null(triggers.Observe(hud, [], battle));
        Assert.Null(triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(100)));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(0.5, false)]
    [InlineData(0.7, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(double.NaN, true)]
    [InlineData(double.PositiveInfinity, true)]
    public void FrequencyIsAValidatedProbability(double frequency, bool plays)
    {
        var triggers = new BobVoiceTriggers(new ChatterRandom(0.6)) { ChatterFrequency = frequency };
        var hud = new GarrisonHud(5, null, null);
        Assert.Null(triggers.Observe(hud, [], new(Preparation: true), TimeSpan.Zero));
        var cue = triggers.Observe(hud, [], new(Preparation: true), TimeSpan.FromSeconds(5));
        Assert.Equal(plays ? BobVoiceCue.PreparationChatter : (BobVoiceCue?)null, cue);
    }

    private sealed class ChatterRandom(params double[] samples) : Random
    {
        private readonly Queue<double> _samples = new(samples);
        public bool UseMaximumDelay { get; init; }
        public override int Next(int minValue, int maxValue) => UseMaximumDelay ? maxValue - 1 : minValue;
        public override double NextDouble() => _samples.Dequeue();
    }
}
