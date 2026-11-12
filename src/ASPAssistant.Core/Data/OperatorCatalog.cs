using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

public sealed class OperatorCatalog(
    IReadOnlyList<Operator> operators, Func<Task<List<Operator>>> loadSelfSelected)
{
    private readonly Dictionary<string, Operator> _baseByName = operators
        .ToDictionary(op => op.Name, StringComparer.Ordinal);
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private volatile IReadOnlyList<Operator>? _selfSelected;

    public IReadOnlyList<Operator> SelfSelected => _selfSelected ?? [];

    public Operator? Find(string name) => _baseByName.GetValueOrDefault(name)
        ?? SelfSelected.FirstOrDefault(op => op.Name == name);

    public string? FindPortrait(string name) => Find(name)?.IconPath
        ?? (name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            ? $"icons/operators/{name}.png" : null);

    public Task LoadForNamesAsync(IEnumerable<string> names) =>
        names.Any(name => !_baseByName.ContainsKey(name)) ? LoadSelfSelectedAsync() : Task.CompletedTask;

    public async Task LoadSelfSelectedAsync()
    {
        if (_selfSelected is not null) return;
        await _loadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_selfSelected is null)
                _selfSelected = await loadSelfSelected().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.Error("OperatorCatalog", "Could not load self-selected operators", ex);
        }
        finally { _loadGate.Release(); }
    }
}
