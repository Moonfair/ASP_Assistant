using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public sealed class GarrisonHistoryTests
{
    [Theory]
    [InlineData("https://zonai.skland.com/web/v1/game/garrison/guide/history-log?seasonId=act3autochess", true)]
    [InlineData("https://zonai.skland.com/api/v1/game/garrison/guide/history-log?seasonId=act3autochess", false)]
    [InlineData("https://zonai.skland.com.evil.example/web/v1/game/garrison/guide/history-log", false)]
    [InlineData("http://zonai.skland.com/web/v1/game/garrison/guide/history-log", false)]
    public void ReadsOnlyOfficialWebHistoryResponses(string url, bool expected)
        => Assert.Equal(expected, GarrisonHistory.IsHistoryEndpoint(url));

    [Fact]
    public async Task OfficialRecordPreservesMemberEquipmentAndStrategyInShare()
    {
        using var history = Json("""
            {"code":0,"data":{"records":[{"id":"record-1","activityId":"act3autochess","versionId":"V3_1",
              "completeState":true,"gameEndTs":"1794459000","modeType":"MULTI","modeDifficulty":"HARD","passRound":11,
              "bandId":"band1","chessSquad":[{"charId":"char_a","equips":[{"equipId":"item1"},{"equipId":"item2"}]},
                {"charId":"char_b","equips":[{"equipId":"item2"}]}]}]}}
            """);
        using var resources = Json("""
            {"code":0,"data":{"chars":[{"charId":"char_a","name":"蓝毒"},{"charId":"char_b","name":"砾"}],
              "items":[{"itemId":"item1","name":"装备甲"},{"itemId":"item2","name":"装备乙"}],
              "strategies":[{"strategyId":"band1","name":"策略甲"}]}}
            """);
        var record = Assert.Single(await GarrisonHistory.ReadRecordsAsync(history));
        var catalog = await GarrisonHistory.ReadCatalogAsync(resources);
        var service = new LineupShareService(["蓝毒", "砾"], ["装备甲", "装备乙"]);
        var converter = new GarrisonHistoryLineupConverter([new() { Name = "蓝毒" }, new() { Name = "砾" }], ["装备甲", "装备乙"], ["策略甲"]);
        var export = converter.Convert(record, catalog);
        Assert.Empty(export.Omissions);

        var lineup = service.Import(service.Export(Assert.IsType<ASPAssistant.Core.Models.Lineup>(export.Lineup)));
        Assert.Equal("MULTI", record.Mode);
        Assert.Equal(11, record.PassedRounds);
        Assert.Equal("策略甲", Assert.Single(lineup.AvailableStrategies));
        Assert.Collection(lineup.Members,
            member =>
            {
                Assert.Equal("蓝毒", member.OperatorName);
                Assert.Equal("装备甲", member.Equipment1);
                Assert.Equal("装备乙", member.Equipment2);
            },
            member =>
            {
                Assert.Equal("砾", member.OperatorName);
                Assert.Equal("装备乙", member.Equipment1);
            });
    }

    [Fact]
    public async Task IncompatibleMembersAreReportedAndCannotMakeAnEmptyShare()
    {
        using var history = Json("""
            {"code":0,"data":{"records":[{"id":"record-2","chessSquad":[{"charId":"char_unknown"}]}]}}
            """);
        using var resources = Json("""
            {"code":0,"data":{"chars":[{"charId":"char_unknown","name":"其他赛季干员"}],"items":[],"strategies":[]}}
            """);
        var record = Assert.Single(await GarrisonHistory.ReadRecordsAsync(history));
        var catalog = await GarrisonHistory.ReadCatalogAsync(resources);
        var converter = new GarrisonHistoryLineupConverter([new() { Name = "砾" }], [], []);
        var export = converter.Convert(record, catalog);
        Assert.Null(export.Lineup);
        Assert.Contains(export.Omissions, message => message.Contains("其他赛季干员"));
    }

    [Fact]
    public async Task UnauthorizedAndMalformedResponsesAreNotReportedAsEmptyHistory()
    {
        using var unauthorized = Json("""{"code":10002,"message":"用户未登录"}""");
        using var malformed = Json("""{"code":0,"data":{}}""");
        using var empty = Json("""{"code":0,"data":{"records":[]}}""");
        await Assert.ThrowsAsync<FormatException>(() => GarrisonHistory.ReadRecordsAsync(unauthorized));
        await Assert.ThrowsAsync<FormatException>(() => GarrisonHistory.ReadRecordsAsync(malformed));
        Assert.Empty(await GarrisonHistory.ReadRecordsAsync(empty));
    }

    [Fact]
    public async Task EliteOperatorKeepsItsFourEquipmentSlotsInAnImportableLineup()
    {
        using var history = Json("""
            {"code":0,"data":{"records":[{"id":"record-3","chessSquad":[
              {"charId":"char_a","mergeLevel":1,"equips":[{"equipId":"item1"},{"equipId":"item2"},
                {"equipId":"item3"},{"equipId":"item4"}]}]}]}}
            """);
        using var resources = Json("""
            {"code":0,"data":{"chars":[{"charId":"char_a","name":"凛御银灰"}],"items":[
              {"itemId":"item1","name":"甲"},{"itemId":"item2","name":"乙"},
              {"itemId":"item3","name":"丙"},{"itemId":"item4","name":"丁"}],"strategies":[]}}
            """);
        var operators = new Operator[] { new() { Name = "凛御银灰", EquipmentSlotCount = 4 } };
        string[] equipment = ["甲", "乙", "丙", "丁"];
        var record = Assert.Single(await GarrisonHistory.ReadRecordsAsync(history));
        var catalog = await GarrisonHistory.ReadCatalogAsync(resources);
        var service = LineupShareService.FromCatalog(operators, equipment);
        var converter = new GarrisonHistoryLineupConverter(operators, equipment, []);
        var export = converter.Convert(record, catalog);
        Assert.Empty(export.Omissions);
        var member = Assert.Single(service.Import(service.Export(Assert.IsType<Lineup>(export.Lineup))).Members);
        Assert.True(member.IsElite);
        Assert.Equal(equipment, member.EquipmentSlots);
    }

    private static MemoryStream Json(string value) => new(System.Text.Encoding.UTF8.GetBytes(value));
}
