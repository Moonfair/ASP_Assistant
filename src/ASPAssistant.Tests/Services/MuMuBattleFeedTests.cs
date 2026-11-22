using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public class MuMuBattleFeedTests
{
    [Fact]
    public void BossExtensionUsesTheSameBoardWithoutPretendingToProvideWindowsHud()
    {
        var update = MuMuBattleFeed.Decode("frame\t1\t50\t4294967296\t0\tchar_a:300000:0:0:0|6,14,1,act3autochess,1000000;2,chess_char_5_22_a,69000;1,chess_char_6_08_a,22000");
        Assert.Equal(300000, update.Sample!.Operators["char_a"].Damage);
        Assert.Null(update.Garrison);
        var stage = System.Text.Json.JsonSerializer.SerializeToElement(update).GetProperty("Stage");
        Assert.Equal(6, stage.GetProperty("State").GetInt32());
        Assert.Equal(14, stage.GetProperty("Round").GetInt32());
        Assert.Equal(69000, stage.GetProperty("BossDamage").GetProperty("Rows")[0].GetProperty("Damage").GetInt32());
        Assert.Equal(GameTargetKind.MuMu, update.Source);
        Assert.Null(MuMuBattleFeed.Decode("idle|4,13,1,act3autochess,0").Sample);
        // An invalid optional section never discards valid global counters.
        Assert.Equal(300000, MuMuBattleFeed.Decode("frame\t1\t50\t0\t0\tchar_a:300000:0:0:0|6,14,BAD").Sample!.Operators["char_a"].Damage);
    }

    [Fact]
    public void SampleUsesExistingPcDamageStatisticsAndGameClock()
    {
        var frame = Assert.IsType<BattleFrame>(MuMuBattleFeed.Decode(
            "frame\t3\t41\t6442450944\t1\tchar_a:10.5:20:30:40\tchar_b:0:0:0:100.5").Sample);
        Assert.Equal((3, 41u, 1.5, true), (frame.Round, frame.Frame, frame.GameSeconds, frame.Ended));
        var stats = new BattleStatistics();
        stats.Update(frame);
        Assert.Equal(201, stats.TotalDamage);
        Assert.Equal(0.5, stats.Rows[0].DamageShare);
        Assert.Equal(new OperatorCounters(10.5,20,30,40), frame.Operators["char_a"]);
    }

    [Theory]
    [InlineData("frame\t1\t0\t0\t0\tchar_a:0:NaN:0:0")]
    [InlineData("frame\t1\t0\t0\t0\tchar_a:1:0:0:0\tchar_a:2:0:0:0")]
    [InlineData("frame\t1\t0\t-1\t0")]
    [InlineData("frame\t1\t0\t0\t0\tchar_a:1:0:0")]
    [InlineData("frame\t1\t0\t0\t0\ttrap_1:1:2:3:4")]
    public void RejectsMalformedLiveFrame(string wire) =>
        Assert.Throws<InvalidDataException>(() => MuMuBattleFeed.Decode(wire));

    [Theory]
    [InlineData("idle", BattleFeedState.WaitingForRound)]
    [InlineData("retry", BattleFeedState.ReadInterrupted)]
    [InlineData("unsupported", BattleFeedState.UnsupportedBuild)]
    [InlineData("permission", BattleFeedState.PermissionRequired)]
    [InlineData("absent", BattleFeedState.WaitingForClient)]
    public void StatesNeverFabricateAnEmptyFrame(string wire, BattleFeedState state)
    {
        var update = MuMuBattleFeed.Decode(wire);
        Assert.Equal(state, update.State);
        Assert.Equal(GameTargetKind.MuMu, update.Source);
        Assert.Null(update.Sample);
    }

    [Theory]
    [InlineData(GameTargetKind.MuMu, "203.0.113.10:5555")]
    [InlineData(GameTargetKind.LdPlayer, "127.0.0.1:16384")]
    public async Task RejectsOtherTargetsBeforeStartingAdb(GameTargetKind kind, string serial)
    {
        var target = new GameTarget("test", "test", kind, 1, 1, Environment.ProcessPath, serial);
        var reports = new List<BattleUpdate>();
        await MuMuBattleFeed.ConnectAsync(target, u => { reports.Add(u); return Task.CompletedTask; }, default);
        var update = Assert.Single(reports);
        Assert.Equal(BattleFeedState.ConnectionFailed, update.State);
        Assert.Equal(GameTargetKind.MuMu, update.Source);
    }
}
