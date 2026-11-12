using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Services;

public static class GarrisonHistory
{
    private const string ApiBase = "https://zonai.skland.com/web/v1/game/garrison/guide/";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    internal static Uri HistoryUri(string season) =>
        new(ApiBase + $"history-log?seasonId={Uri.EscapeDataString(season)}");

    public static Uri CatalogUri(string? season = null, string? version = null) =>
        string.IsNullOrEmpty(season) || string.IsNullOrEmpty(version)
            ? new Uri(ApiBase + "resource")
            : new Uri(ApiBase + $"resource?seasonId={Uri.EscapeDataString(season)}&versionId={Uri.EscapeDataString(version)}");

    public static async Task<IReadOnlyList<GarrisonRecord>> ReadRecordsAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var data = await ReadDataAsync<HistoryData>(stream, cancellationToken);
        var records = data.Records ?? throw new FormatException("森空岛响应中缺少战绩列表。");
        return records.OfType<GarrisonRecord>()
            .Where(record => !string.IsNullOrWhiteSpace(record.Id))
            .DistinctBy(record => record.Id, StringComparer.Ordinal)
            .OrderByDescending(record => record.EndedAt)
            .ToArray();
    }

    public static async Task<GarrisonCatalog> ReadCatalogAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var data = await ReadDataAsync<CatalogData>(stream, cancellationToken);
        if (data.Chars is null || data.Items is null || data.Strategies is null)
            throw new FormatException("森空岛响应中缺少赛季干员、装备或策略数据。");

        return new GarrisonCatalog(
            Names(data.Chars, item => item.CharId),
            Names(data.Items, item => item.ItemId),
            Names(data.Strategies, item => item.StrategyId), data.VersionInfo?.SeasonId, data.VersionInfo?.VersionId);
    }

    internal static async Task<T> ReadDataAsync<T>(Stream stream, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var response = await JsonSerializer.DeserializeAsync<Response<T>>(stream, Options, cancellationToken);
            if (response?.Code == 10002) throw new FormatException("战绩授权已失效，请重新使用鹰角通行证授权。");
            if (response?.Code != 0 || response.Data is null)
                throw new FormatException("森空岛未返回有效数据，请稍后重试。");
            return response.Data;
        }
        catch (JsonException ex)
        {
            throw new FormatException("森空岛响应格式有变化，请稍后重试。", ex);
        }
    }

    private static Dictionary<string, string> Names(IEnumerable<ResourceItem?> items, Func<ResourceItem, string?> id) =>
        items.OfType<ResourceItem>()
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(id(item)))
            .DistinctBy(id, StringComparer.Ordinal)
            .ToDictionary(item => id(item)!, item => item.Name!, StringComparer.Ordinal);

    private sealed record Response<T>(int? Code, T? Data);
    private sealed record HistoryData(List<GarrisonRecord?>? Records);
    private sealed record CatalogData(List<ResourceItem?>? Chars, List<ResourceItem?>? Items, List<ResourceItem?>? Strategies, SeasonVersion? VersionInfo);
    private sealed record SeasonVersion(string? SeasonId, string? VersionId);
    private sealed record ResourceItem(string? CharId, string? ItemId, string? StrategyId, string? Name);
}

public sealed record GarrisonRecord(
    string Id,
    [property: JsonPropertyName("activityId")] string? SeasonId,
    string? VersionId,
    [property: JsonPropertyName("gameEndTs")] long? EndTimestamp,
    [property: JsonPropertyName("modeType")] string? Mode,
    [property: JsonPropertyName("modeDifficulty")] string? Difficulty,
    [property: JsonPropertyName("passRound")] int? PassedRounds,
    [property: JsonPropertyName("bandId")] string? StrategyId,
    [property: JsonPropertyName("chessSquad")] List<GarrisonRecordMember?>? Squad)
{
    public DateTimeOffset? EndedAt => EndTimestamp is >= 1 and <= 253402300799
        ? DateTimeOffset.FromUnixTimeSeconds(EndTimestamp.Value)
        : null;

    public string ModeName => Mode switch
    {
        "LOCAL" => "教学",
        "SINGLE" => "单人",
        "MULTI" => "联机",
        _ => Mode ?? ""
    };
}

public sealed record GarrisonRecordMember(string? CharId, int? MergeLevel, List<GarrisonRecordEquipment?>? Equips);
public sealed record GarrisonRecordEquipment(string? EquipId);

public sealed record GarrisonCatalog(
    IReadOnlyDictionary<string, string> Operators,
    IReadOnlyDictionary<string, string> Equipment,
    IReadOnlyDictionary<string, string> Strategies,
    string? SeasonId,
    string? VersionId);
