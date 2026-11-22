namespace ASPAssistant.App.Views;

internal static class GarrisonChessLabel
{
    internal static string Resolve(string id, string? activity, Dictionary<string, string>? selected,
        IReadOnlyDictionary<string, Dictionary<string, string>> catalog, IReadOnlyDictionary<string, string> operators)
    {
        if (selected != null)
        {
            if (selected.TryGetValue(id, out var character)) return operators.GetValueOrDefault(character, character);
            if (id.EndsWith("_b", StringComparison.Ordinal) && selected.TryGetValue(id[..^1] + "a", out character))
                return operators.GetValueOrDefault(character, character) + "（精锐）";
        }
        return activity != null && catalog.TryGetValue(activity, out var season) ? season.GetValueOrDefault(id, id) : id;
    }
}
