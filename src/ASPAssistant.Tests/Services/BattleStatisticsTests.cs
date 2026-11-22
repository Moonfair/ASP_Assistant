using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public class BattleStatisticsTests
{
    private const string Ironhide = "char_618_acrbot";
    private const string Specter = "char_143_ghost";

    [Fact]
    public void FirstSnapshotUsesAllDamageTypesBeforeRoundingAndRanksByDamage()
    {
        var meter = new BattleStatistics();
        meter.Update(new(1, 438, 14.6, false, new()
        {
            [Specter] = new(0.25, 0.5, 1, 0.75),
            [Ironhide] = new(7.5, 0, 0, 0)
        }));

        Assert.Equal(10, meter.TotalDamage);
        Assert.Collection(meter.Rows,
            row => { Assert.Equal(Ironhide, row.Id); Assert.Equal(0.75, row.DamageShare); },
            row => { Assert.Equal(Specter, row.Id); Assert.Equal(0.25, row.DamageShare); });
    }

    [Fact]
    public void ZeroDamageRoundGivesEveryOperatorAZeroShare()
    {
        var meter = new BattleStatistics();
        meter.Update(new(1, 30, 1, false, new()
        {
            [Ironhide] = new(0, 0, 0, 0), [Specter] = new(0, 0, 0, 0)
        }));

        Assert.Equal(0, meter.TotalDamage);
        Assert.Equal(2, meter.Rows.Count);
        Assert.All(meter.Rows, row => Assert.Equal(0, row.DamageShare));
    }

    [Fact]
    public void FinalCorrectionAndNewRoundReplacePreviousTotals()
    {
        var meter = new BattleStatistics();
        var frame = new BattleFrame(1, 90, 3, false, new()
        {
            [Ironhide] = new(20, 0, 0, 0), [Specter] = new(20, 0, 0, 0)
        });
        meter.Update(frame);
        meter.Update(frame);
        Assert.Equal(40, meter.TotalDamage);

        meter.Update(frame with { Ended = true, Operators = new(frame.Operators) { [Ironhide] = new(60, 0, 0, 0) } });
        Assert.Equal(80, meter.TotalDamage);
        Assert.Equal(0.75, meter.Rows[0].DamageShare);

        meter.Update(new(2, 15, 0.5, false, new() { [Specter] = new(5, 0, 0, 0) }));
        Assert.Equal(5, meter.TotalDamage);
        Assert.Equal(1, Assert.Single(meter.Rows).DamageShare);
        meter.Reset();
        Assert.Empty(meter.Rows);
        Assert.Equal(0, meter.TotalDamage);
    }
}
