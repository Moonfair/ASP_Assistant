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

    /// <summary>
    /// Loads covenant definitions and the JobChange-equipment → covenant map from
    /// <c>data/covenants.json</c>. If <c>data/covenants.user.json</c> exists, merges its
    /// entries on top (user file overrides matching covenant entries by name and adds /
    /// overrides job-change-equipment mappings).
    /// </summary>
    public async Task<(List<CovenantInfo> Covenants, Dictionary<string, string> JobChangeEquipmentToCovenant)>
        LoadCovenantsAsync()
    {
        var path = Path.Combine(_dataDirectory, "covenants.json");
        if (!File.Exists(path))
            return ([], []);

        var json = await File.ReadAllTextAsync(path);
        var wrapper = JsonSerializer.Deserialize<CovenantDataFile>(json, JsonOptions)
                      ?? new CovenantDataFile();

        var covenants = wrapper.Covenants;
        var jobChangeMap = wrapper.JobChangeEquipmentToCovenant ?? [];

        var userPath = Path.Combine(_dataDirectory, "covenants.user.json");
        if (File.Exists(userPath))
        {
            var userJson = await File.ReadAllTextAsync(userPath);
            var userWrapper = JsonSerializer.Deserialize<CovenantDataFile>(userJson, JsonOptions);
            if (userWrapper is not null)
            {
                var byName = covenants.ToDictionary(c => c.Name, c => c);
                foreach (var uc in userWrapper.Covenants)
                {
                    if (string.IsNullOrEmpty(uc.Name)) continue;
                    if (byName.TryGetValue(uc.Name, out var existing))
                    {
                        if (uc.ActivateCount.HasValue) existing.ActivateCount = uc.ActivateCount;
                        if (!string.IsNullOrEmpty(uc.BondType)) existing.BondType = uc.BondType;
                    }
                    else
                    {
                        covenants.Add(uc);
                    }
                }
                if (userWrapper.JobChangeEquipmentToCovenant is not null)
                {
                    foreach (var (k, v) in userWrapper.JobChangeEquipmentToCovenant)
                        jobChangeMap[k] = v;
                }
            }
        }

        return (covenants, jobChangeMap);
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

    private class CovenantDataFile
    {
        public List<CovenantInfo> Covenants { get; set; } = [];
        public Dictionary<string, string>? JobChangeEquipmentToCovenant { get; set; }
    }
}
