using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

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

    public bool LoadSideFilterLayout()
    {
        try
        {
            var path = Path.Combine(_settingsDir, "ui.json");
            if (!File.Exists(path)) return true;
            return JsonSerializer.Deserialize<LayoutPreferences>(File.ReadAllText(path), JsonOptions)?.SideFilters ?? true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Settings", "Could not read layout preference; using side filters", ex);
            return true;
        }
    }

    // The small preference is saved synchronously, so closing immediately after a click cannot lose it.
    public void SaveSideFilterLayout(bool sideFilters) => SavePreference("ui", new LayoutPreferences(sideFilters));

    public MarkerOffset LoadMarkerOffset()
    {
        try
        {
            var path = Path.Combine(_settingsDir, "marker-offset.json");
            if (File.Exists(path) &&
                JsonSerializer.Deserialize<MarkerOffset?>(File.ReadAllText(path), JsonOptions) is { } offset &&
                Math.Abs(offset.X) <= 1 && Math.Abs(offset.Y) <= 1)
                return offset;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Settings", "Could not read marker offset", ex);
        }
        return default;
    }

    public void SaveMarkerOffset(MarkerOffset offset) => SavePreference("marker-offset", offset);

    private sealed record LayoutPreferences(bool SideFilters = true);

    public GameTargetPreference LoadGameTarget()
    {
        try
        {
            var path = Path.Combine(_settingsDir, "game-target.json");
            if (File.Exists(path) &&
                JsonSerializer.Deserialize<GameTargetPreference>(File.ReadAllText(path), JsonOptions) is { } saved &&
                !string.IsNullOrWhiteSpace(saved.Id))
                return saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.Error("Settings", "Could not read game target preference", ex);
        }
        return new GameTargetPreference();
    }

    public void SaveGameTarget(GameTargetPreference preference) => SavePreference("game-target", preference);

    private void SavePreference<T>(string name, T preference)
    {
        Directory.CreateDirectory(_settingsDir);
        var temporary = Path.Combine(_settingsDir, $"{name}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preference, JsonOptions));
            File.Move(temporary, Path.Combine(_settingsDir, $"{name}.json"), overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task SaveTrackingEntriesAsync(List<TrackingEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        await _ioGate.WaitAsync().ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var path = Path.Combine(_settingsDir, "tracking.json");
            temporaryPath = Path.Combine(_settingsDir, $"tracking.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
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
        await _ioGate.WaitAsync().ConfigureAwait(false);
        var path = Path.Combine(_settingsDir, "tracking.json");
        try
        {
            if (!File.Exists(path)) return [];
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
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
