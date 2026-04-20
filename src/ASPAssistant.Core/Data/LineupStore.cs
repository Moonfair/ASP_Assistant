using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

/// <summary>
/// 阵容文件持久化：每个阵容一个 &lt;id&gt;.json 文件，存于
/// %AppData%\ASPAssistant\lineups\，避免单文件并发风险。
/// </summary>
public class LineupStore
{
    private readonly string _dir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public LineupStore(string lineupsDir)
    {
        _dir = lineupsDir;
    }

    public async Task<List<Lineup>> LoadAllAsync()
    {
        if (!Directory.Exists(_dir))
            return [];

        var files = Directory.EnumerateFiles(_dir, "*.json");
        var result = new List<Lineup>();
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var lineup = JsonSerializer.Deserialize<Lineup>(json, JsonOptions);
                if (lineup is null) continue;
                if (string.IsNullOrEmpty(lineup.Id))
                    lineup.Id = Path.GetFileNameWithoutExtension(file);
                result.Add(lineup);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("LineupStore", $"Failed to load {file}: {ex.Message}");
            }
        }
        return [.. result.OrderByDescending(l => l.UpdatedAt)];
    }

    public async Task SaveAsync(Lineup lineup)
    {
        Directory.CreateDirectory(_dir);
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        lineup.UpdatedAt = DateTime.UtcNow;

        var path = Path.Combine(_dir, $"{lineup.Id}.json");
        var json = JsonSerializer.Serialize(lineup, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public Task DeleteAsync(string id)
    {
        var path = Path.Combine(_dir, $"{id}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }
}
