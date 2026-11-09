using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

public class SettingsManager
{
    private readonly string _settingsDir;
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsManager(string settingsDir)
    {
        _settingsDir = settingsDir;
    }

    public async Task SaveTrackingEntriesAsync(List<TrackingEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        await _ioGate.WaitAsync();
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var path = Path.Combine(_settingsDir, "tracking.json");
            temporaryPath = Path.Combine(_settingsDir, $"tracking.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporaryPath, json);
            // A sibling rename keeps readers from observing a truncated/partial JSON file.
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { if (temporaryPath != null) File.Delete(temporaryPath); }
            finally { _ioGate.Release(); }
        }
    }

    public async Task<List<TrackingEntry>> LoadTrackingEntriesAsync()
    {
        await _ioGate.WaitAsync();
        var path = Path.Combine(_settingsDir, "tracking.json");
        try
        {
            if (!File.Exists(path)) return [];
            var json = await File.ReadAllTextAsync(path);
            var entries = JsonSerializer.Deserialize<List<TrackingEntry>>(json, JsonOptions);
            if (entries == null || entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Name) || !Enum.IsDefined(e.Type)))
                throw new JsonException("Invalid tracking entries.");
            return entries;
        }
        catch (JsonException ex)
        {
            AppLogger.Error("Settings", "Invalid tracking file; restoring defaults", ex);
            try
            {
                File.Move(path, Path.Combine(_settingsDir, $"tracking.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json"));
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                AppLogger.Error("Settings", "Could not preserve invalid tracking file", backupError);
            }
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Settings", "Could not read tracking file; using defaults", ex);
            return [];
        }
        finally { _ioGate.Release(); }
    }

}
