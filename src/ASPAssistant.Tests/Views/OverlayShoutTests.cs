using ASPAssistant.App.Services;
using ASPAssistant.App.Windows;

namespace ASPAssistant.Tests.Views;

public class OverlayShoutTests
{
    [Fact]
    public void OnlyTheNewestFreshUnmutedTeammateShoutIsShownWithoutReplayingOldSnapshots()
    {
        var state = new MatchSnapshot
        {
            Phase = "playing", SelfId = "me", Muted = ["muted"],
            Shouts = [new("1", "ally", "old", 1000), new("2", "ally", "current", 1200),
                new("3", "muted", "silent", 1300), new("4", "me", "own", 1350)]
        };
        Assert.Equal("current", OverlayShout.Latest(state, 7199)?.Text);
        Assert.Null(OverlayShout.Latest(state, 7200));
        Assert.Null(OverlayShout.Latest(state with { Phase = "lobby" }, 1350));
    }
}
