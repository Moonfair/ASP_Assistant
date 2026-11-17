using ASPAssistant.Mac;

namespace ASPAssistant.Mac.Tests;

public class MacAdbClientTests
{
    [Fact]
    public void ParsesOnlyReadyDevices()
    {
        const string output = "List of devices attached\n"
            + "emulator-5554\tdevice product:sdk model:Pixel_8\n"
            + "127.0.0.1:5555\toffline\n"
            + "emulator-5556\tunauthorized\n";

        Assert.Equal(["emulator-5554"], MacAdbClient.ParseDevices(output));
    }

    [Fact]
    public void RejectsNonPngScreenshot()
    {
        Assert.False(MacAdbClient.IsPng("error"u8.ToArray()));
        Assert.True(MacAdbClient.IsPng([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82,
            0, 0, 0, 1, 0, 0, 0, 1]));
    }
}
