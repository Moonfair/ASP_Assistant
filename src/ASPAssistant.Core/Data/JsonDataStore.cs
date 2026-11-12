using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

public class JsonDataStore
{
    private readonly string _dataDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JsonDataStore(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public async Task<List<Operator>> LoadOperatorsAsync()
    {
        var path = Path.Combine(_dataDirectory, "operators.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        var wrapper = JsonSerializer.Deserialize<OperatorDataFile>(json, JsonOptions);
        return wrapper?.Operators ?? [];
    }

    public async Task<(List<Equipment> Equipment, List<string> ManualJobChangeEquipments)> LoadEquipmentAsync()
    {
        var path = Path.Combine(_dataDirectory, "equipment.json");
        if (!File.Exists(path))
            return ([], []);

        var json = await File.ReadAllTextAsync(path);
        var wrapper = JsonSerializer.Deserialize<EquipmentDataFile>(json, JsonOptions);
        return (wrapper?.Equipment ?? [], wrapper?.ManualJobChangeEquipments ?? []);
    }

    public async Task<CovenantCatalog> LoadCovenantCatalogAsync()
    {
        var path = Path.Combine(_dataDirectory, "covenants.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<CovenantCatalog>(await File.ReadAllTextAsync(path), JsonOptions) ?? new();
    }

    public async Task<List<Strategy>> LoadStrategiesAsync()
    {
        var path = Path.Combine(_dataDirectory, "strategies.json");
        if (!File.Exists(path)) return [];
        return JsonSerializer.Deserialize<StrategyDataFile>(await File.ReadAllTextAsync(path), JsonOptions)?.Strategies ?? [];
    }

    /// <summary>
    /// Loads the skin avatar map produced by <c>fetch_spdatabase.py --download-skins</c>.
    /// Returns a dictionary of { filename → operator name }, e.g.
    /// { "char_1012_skadi2_boc_4.png" → "浊心斯卡蒂" }.
    /// Returns an empty dictionary if the file does not exist.
    /// </summary>
    public async Task<Dictionary<string, string>> LoadSkinAvatarMapAsync()
    {
        var path = Path.Combine(_dataDirectory, "skin_avatar_map.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
    }

    public async Task<CatalogDetails> LoadCatalogDetailsAsync()
    {
        var skills = await ReadSupplementAsync<OperatorSkillDataFile>("operator_skills.json");
        var tips = await ReadSupplementAsync<CatalogTips>("catalog_tips.json");
        var source = await ReadSupplementAsync<CatalogSource>("source_metadata.json");
        var season = source?.SeasonCode;
        return new(season, season == skills?.Season ? skills?.Operators ?? [] : [],
            season == tips?.Season && source?.VersionCode == tips?.Version ? tips : null);
    }

    private async Task<T?> ReadSupplementAsync<T>(string filename) where T : class
    {
        var path = Path.Combine(_dataDirectory, filename);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Catalog", $"Could not load {filename}: {ex.GetType().Name}");
            return null;
        }
    }

    private sealed record OperatorSkillDataFile(string Season, Dictionary<string, List<OperatorSkill>> Operators);
    private sealed record CatalogSource(string SeasonCode, string? VersionCode);

    private class OperatorDataFile
    {
        public List<Operator> Operators { get; set; } = [];
    }

    private class EquipmentDataFile
    {
        public List<Equipment> Equipment { get; set; } = [];
        public List<string> ManualJobChangeEquipments { get; set; } = [];
    }

    private sealed record StrategyDataFile(List<Strategy> Strategies);
}
