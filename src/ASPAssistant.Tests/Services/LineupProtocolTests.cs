using System.Threading.Channels;
using ASPAssistant.App.Services;

namespace ASPAssistant.Tests.Services;

public class LineupProtocolTests
{
    private const string Hash = "b281796e53b2234e8e7deb064521fbcaef39dfc2fc01f3627605feee22c71a8a";
    private const string Prefix = "aspassistant://import-lineup/?sha256=";

    [Fact]
    public void ActivationAcceptsOnlyTheImportRouteAndOneHexDigest()
    {
        Assert.Equal(Hash, LineupProtocol.ParseImport(["--lineup-uri", Prefix + Hash]));
        Assert.Null(LineupProtocol.ParseImport([]));
        Assert.Null(LineupProtocol.ParseImport(["--lineup-uri", "https://import-lineup/?sha256=" + Hash]));
        Assert.Null(LineupProtocol.ParseImport(["--lineup-uri", Prefix + Hash + "&extra=1"]));
        Assert.Null(LineupProtocol.ParseImport(["--lineup-uri", Prefix + new string('z', 64)]));
    }

    [Fact]
    public void ClipboardLineEndingsAreAcceptedButDifferentTextIsRejected()
    {
        Assert.Equal("ASPA-LINEUP/2\n阵容", LineupProtocol.RequireMatchingShare("ASPA-LINEUP/2\r\n阵容", Hash));
        Assert.Throws<FormatException>(() => LineupProtocol.RequireMatchingShare("ASPA-LINEUP/2\n其他阵容", Hash));
    }

    [Fact]
    public async Task RunningInstanceAcceptsSuccessiveLaunches()
    {
        var name = "ASPAssistant.Tests." + Guid.NewGuid();
        var primary = new LineupActivation(name);
        using var secondary = new LineupActivation(name);
        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);

        var received = Channel.CreateUnbounded<string?>();
        var listener = primary.ListenAsync(hash => received.Writer.WriteAsync(hash).AsTask());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await secondary.ForwardAsync(Hash);
            Assert.Equal(Hash, await received.Reader.ReadAsync(timeout.Token));
            await secondary.ForwardAsync(null);
            Assert.Null(await received.Reader.ReadAsync(timeout.Token));
        }
        finally
        {
            primary.Dispose();
            await listener;
        }
    }
}
