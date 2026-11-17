using ASPAssistant.Core.Interop;

namespace ASPAssistant.Mac.Tests;

public class MacWindowBridgeTests
{
    [Fact]
    public void ParsesGameWindowCoordinatesAndForeground()
    {
        const string json = "{\"frontmostPid\":42,\"windows\":[{\"id\":7,\"pid\":42,\"title\":\"明日方舟\",\"owner\":\"Arknights\",\"x\":100,\"y\":200,\"width\":1280,\"height\":720}]}";
        var snapshot = MacWindowBridge.Parse(json);

        Assert.Equal((nint)7, snapshot.ForegroundWindow);
        Assert.Equal(100, snapshot.Windows[0].Bounds.Left);
        Assert.Equal(920, snapshot.Windows[0].Bounds.Bottom);
    }

    [Fact]
    public void FindsNativeGameByOwnerWhenWindowTitleIsHidden()
    {
        const string json = "{\"frontmostPid\":42,\"windows\":[{\"id\":7,\"pid\":42,\"title\":\"\",\"owner\":\"明日方舟\",\"x\":100,\"y\":200,\"width\":1280,\"height\":720}]}";
        var snapshot = MacWindowBridge.Parse(json);

        Assert.Equal((nint)7, MacWindowBridge.FindArknightsWindow(snapshot)?.Id);
    }
}
