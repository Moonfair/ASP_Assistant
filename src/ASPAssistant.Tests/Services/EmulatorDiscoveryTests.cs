using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using FluentAssertions;
using System.Net;
using System.Net.Sockets;

namespace ASPAssistant.Tests.Services;

public class EmulatorDiscoveryTests
{
    [Fact]
    public void MuMuKeepsTheWindowAndEndpointFromEachRunningInstance()
    {
        const string output = """
            {
              "0": {"index":"0","name":"第一个","is_process_started":true,"main_wnd":"0020AB01","render_wnd":"0020AB02","adb_host_ip":"127.0.0.1","adb_port":16384},
              "1": {"index":"1","name":"第二个","is_process_started":true,"main_wnd":"0030BC01","render_wnd":"0030BC02","adb_host_ip":"127.0.0.1","adb_port":16416},
              "2": {"index":"2","name":"已关闭","is_process_started":false}
            }
            """;

        var targets = EmulatorDiscovery.ParseMuMu(output, @"C:\MuMu", @"C:\MuMu\adb.exe");

        targets.Select(t => (t.Instance, t.Window, t.RenderWindow, t.Serial)).Should().Equal(
            (0, (nint)0x0020AB01, (nint)0x0020AB02, "127.0.0.1:16384"),
            (1, (nint)0x0030BC01, (nint)0x0030BC02, "127.0.0.1:16416"));
    }

    [Fact]
    public void MuMuWithNoEndpointStillProvidesItsRenderWindow()
    {
        const string output = """
            {"index":"0","name":"MuMu","is_process_started":true,"main_wnd":"1234","render_wnd":"5678"}
            """;

        var target = EmulatorDiscovery.ParseMuMu(output, @"C:\MuMu", null).Should().ContainSingle().Subject;
        target.RenderWindow.Should().Be((nint)0x5678);
        target.Serial.Should().BeNull();
    }

    [Fact]
    public void LdPlayerUsesTheInstanceIndexAndSkipsStoppedRows()
    {
        const string output = "0,已关闭,0,0,0,-1,-1,1920,1080,240\r\n"
            + "3,分组,实例,1234,5678,1,800,801,1920,1080,240\r\n"
            + "bad,malformed";

        var target = EmulatorDiscovery.ParseLdPlayer(output, @"C:\LDPlayer", @"C:\LDPlayer\adb.exe")
            .Should().ContainSingle().Subject;

        target.Name.Should().Be("分组,实例");
        target.Window.Should().Be((nint)1234);
        target.RenderWindow.Should().Be((nint)5678);
        target.Serial.Should().Be("emulator-5560");
        target.AlternateSerial.Should().Be("127.0.0.1:5561");
    }

    [Fact]
    public void FittingLandscapeFrameKeepsMarkersOutOfTheBlackBars()
    {
        var client = new RECT { Left = -1200, Top = 50, Right = -240, Bottom = 690 };

        var viewport = GameViewport.Fit(client, 1920, 1080);

        viewport.Should().BeEquivalentTo(new RECT { Left = -1200, Top = 100, Right = -240, Bottom = 640 });
    }

    [Fact]
    public void LoopbackListenerIsAttributedToItsActualOwner()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        LocalTcpListener.FindOwner(port).Should().Be((uint)Environment.ProcessId);
    }

    [Fact]
    public void MatchingManualWindowMustBeUnambiguousAfterRestart()
    {
        const string path = @"C:\Emulator\HD-Player.exe";
        var preference = new GameTargetPreference("adb", "模拟器", path, @"C:\Emulator\HD-Adb.exe", "127.0.0.1:5555");
        var window = new HostWindow(1234, "模拟器", path, 5678);

        var target = GameTargetSelection.FindManualTarget(preference, [window]);
        target.Should().NotBeNull();
        target!.Window.Should().Be((nint)1234);
        GameTargetSelection.FindManualTarget(preference, [window, window with { Handle = 4321 }]).Should().BeNull();
    }
}
