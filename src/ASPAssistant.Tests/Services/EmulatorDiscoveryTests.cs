using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using FluentAssertions;
using System.Net;
using System.Net.Sockets;

namespace ASPAssistant.Tests.Services;

public class EmulatorDiscoveryTests
{
    [Theory]
    [InlineData("    topResumedActivity=ActivityRecord{18e24 u0 com.hypergryph.arknights/.MainActivity t5}", true)]
    [InlineData("    mResumedActivity: ActivityRecord{18e24 u0 com.hypergryph.arknights.bilibili/.MainActivity t5}", true)]
    [InlineData("  ResumedActivity: ActivityRecord{18e24 u0 com.hypergryph.arknights/.MainActivity t5}", true)]
    [InlineData("    Resumed: ActivityRecord{18e24 u0 com.hypergryph.arknights/.MainActivity t5}", true)]
    [InlineData("  * Hist #0: ActivityRecord{18e24 u0 com.hypergryph.arknights/.MainActivity t5}", false)]
    [InlineData("    topResumedActivity=ActivityRecord{18e24 u0 com.hypergryph.arknights.clone/.MainActivity t5}", false)]
    [InlineData("    mResumedActivity: ActivityRecord{18e24 u0 com.hypergryph.arknights/.MainActivity t5}\r\n    topResumedActivity=ActivityRecord{67f u0 com.android.launcher3/.Launcher t7}", false)]
    [InlineData("    topResumedActivity=ActivityRecord{18e24 u0 com.hypergryph.arknights/.MainActivity t5}\r\n  ResumedActivity: ActivityRecord{67f u0 com.android.launcher3/.Launcher t7}", false)]
    public void AutomaticDetectionRequiresTheActualArknightsActivity(string activityDump, bool expected)
    {
        EmulatorDiscovery.IsArknightsResumed(activityDump).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, true, 6, true)]
    [InlineData(null, true, 10, false)]
    [InlineData(null, false, 0, false)]
    [InlineData(false, true, 0, false)]
    [InlineData(true, false, 100, true)]
    public void AutomaticDetectionBrieflyKeepsOnlyTheCurrentEmulatorWhenAdbIsUnavailable(
        bool? active, bool isCurrent, int secondsSinceConfirmation, bool expected)
    {
        EmulatorDiscovery.CanUseAutomaticEmulator(active, isCurrent, TimeSpan.FromSeconds(secondsSinceConfirmation))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(true, true, 1, 2, 1)]
    [InlineData(true, true, 2, 1, 2)]
    [InlineData(true, true, 0, 2, 2)]
    [InlineData(true, true, 0, 0, 1)]
    [InlineData(false, true, 0, 1, 2)]
    [InlineData(true, false, 0, 2, 1)]
    [InlineData(false, false, 0, 2, 0)]
    public void AutomaticSelectionFollowsTheActiveGameAndKeepsItWhenFocusLeaves(
        bool windowsRunning, bool emulatorRunningGame, int foreground, int previous, int expected)
    {
        var windows = new GameTarget("windows", "Windows 客户端", GameTargetKind.Windows, 1, 1);
        var emulator = new GameTarget("mumu:instance:0", "MuMu", GameTargetKind.MuMu, 2, 2);
        var current = previous == 1 ? windows : previous == 2 ? emulator : null;

        var target = GameTargetSelection.ChooseAutomaticTarget(windowsRunning ? windows : null,
            emulatorRunningGame ? [emulator] : [], current, foreground);

        (target?.Window ?? 0).Should().Be((nint)expected);
    }

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
