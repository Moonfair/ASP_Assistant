using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Data;

public sealed class LineupStore(string directory)
{
    private readonly string _path = Path.Combine(directory, "lineups.json");

    public async Task<List<Lineup>> LoadAsync()
    {
        string json;
        try { json = await File.ReadAllTextAsync(_path); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        var content = JsonSerializer.Deserialize<LibraryFile>(json, LineupShareService.JsonOptions);
        if (content is null || content.Version != 1 || content.Lineups is null)
            throw new FormatException("阵容文件格式或版本不支持。");
        foreach (var lineup in content.Lineups) LineupShareService.ValidateStructure(lineup);
        if (content.Lineups.Select(x => x.Id).Distinct().Count() != content.Lineups.Count)
            throw new FormatException("阵容文件包含重复标识。");
        return content.Lineups;
    }

    public async Task SaveAsync(IReadOnlyList<Lineup> lineups)
    {
        foreach (var lineup in lineups) LineupShareService.ValidateStructure(lineup);
        var json = JsonSerializer.Serialize(new LibraryFile(1, lineups.ToList()), LineupShareService.JsonOptions);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"lineups.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record LibraryFile(int Version, List<Lineup> Lineups);
}
