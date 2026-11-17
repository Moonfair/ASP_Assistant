using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public class BobVoiceTriggersTests
{
    [Fact]
    public void OpeningRoundNeedsTwoGoodFramesAndPlaysOnce()
    {
        var triggers = new BobVoiceTriggers();
        Assert.Null(triggers.Observe(new GarrisonHud(1, null, null), []));
        Assert.Null(triggers.Observe(new GarrisonHud(1, 1, 4), []));
        Assert.Equal(BobVoiceCue.Opening, triggers.Observe(new GarrisonHud(1, 1, 4), []));
        triggers.Observe(default, []);
        Assert.Null(triggers.Observe(new GarrisonHud(1, 1, 4), []));
        Assert.Null(triggers.Observe(new GarrisonHud(1, 1, 4), []));
    }

    [Fact]
    public void IgnoresSingleFrameLevelNoiseAndAnnouncesConfirmedIncrease()
    {
        var triggers = new BobVoiceTriggers();
        triggers.Observe(new GarrisonHud(7, 3, 10), []);
        triggers.Observe(new GarrisonHud(7, 3, 10), []);
        Assert.Null(triggers.Observe(new GarrisonHud(7, 4, 6), []));
        Assert.Null(triggers.Observe(new GarrisonHud(7, 3, 10), []));
        Assert.Null(triggers.Observe(new GarrisonHud(7, 4, 6), []));
        Assert.Equal(BobVoiceCue.Upgrade, triggers.Observe(new GarrisonHud(7, 4, 6), []));
        Assert.Null(triggers.Observe(new GarrisonHud(7, 4, 6), []));
    }

    [Fact]
    public void ATrackedNameMustPersistAndIsAnnouncedOncePerRound()
    {
        var triggers = new BobVoiceTriggers();
        var hud = new GarrisonHud(5, 2, 8);
        triggers.Observe(hud, []);
        triggers.Observe(hud, []);
        Assert.Null(triggers.Observe(hud, ["能天使"]));
        Assert.Null(triggers.Observe(hud, []));
        Assert.Null(triggers.Observe(hud, ["能天使"]));
        Assert.Equal(BobVoiceCue.TrackedShopItem, triggers.Observe(hud, ["能天使"]));
        triggers.Observe(default, []);
        Assert.Null(triggers.Observe(hud, ["能天使"]));
        Assert.Null(triggers.Observe(hud, ["能天使"]));
    }

    [Fact]
    public void PreparationResetsOpeningWithoutReplayingOnALateAttach()
    {
        var triggers = new BobVoiceTriggers();
        var midMatch = new GarrisonHud(5, 2, 8);
        Assert.Null(triggers.Observe(midMatch, []));
        Assert.Null(triggers.Observe(midMatch, []));
        triggers.Reset();
        var opening = new GarrisonHud(1, 1, 4);
        Assert.Null(triggers.Observe(opening, []));
        Assert.Equal(BobVoiceCue.Opening, triggers.Observe(opening, []));
    }
}
