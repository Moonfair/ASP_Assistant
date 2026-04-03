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

    private class OperatorDataFile
    {
        public List<Operator> Operators { get; set; } = [];
    }

    private class EquipmentDataFile
    {
        public List<Equipment> Equipment { get; set; } = [];
        public List<string> ManualJobChangeEquipments { get; set; } = [];
    }
}
