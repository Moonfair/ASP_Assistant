using System.IO;
using System.Text.Json;

namespace ASPAssistant.App.Services;

internal sealed record QuickPhrase(string Template, string Word, bool IsRoomNickname = false)
{
    public string Text => Template.Replace("□", Word, StringComparison.Ordinal);
}

/// <summary>The desktop pickers and the relay use the same checked-in whitelist.</summary>
internal static class QuickChatCatalog
{
    private sealed record Catalog(string[] Templates, Dictionary<string, string[]> TemplateGroups, Dictionary<string, string[]> Groups);

    private static readonly Catalog Data = Load();
    private static readonly string[] AllWords = Data.Groups.Values.SelectMany(words => words).Distinct(StringComparer.Ordinal).ToArray();
    private static readonly HashSet<string> WordSet = new(AllWords, StringComparer.Ordinal);
    private static readonly HashSet<string> TemplateSet = new(Data.Templates, StringComparer.Ordinal);

    internal const string RoomNicknameCategory = "房间昵称";
    internal static IReadOnlyList<string> Templates => Data.Templates;
    internal static IReadOnlyDictionary<string, string[]> Groups => Data.Groups;
    internal static IReadOnlyDictionary<string, string[]> TemplateGroups => Data.TemplateGroups;

    private static Catalog Load()
    {
        using var stream = typeof(QuickChatCatalog).Assembly.GetManifestResourceStream("ASPAssistant.QuickChat.json")
            ?? throw new InvalidDataException("找不到快捷喊话词库。");
        return JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("快捷喊话词库为空。");
    }

    internal static bool IsStaticWord(string word) => WordSet.Contains(word);
    internal static bool IsKnownPhrase(QuickPhrase phrase) => TemplateSet.Contains(phrase.Template) &&
        (phrase.IsRoomNickname ? !string.IsNullOrWhiteSpace(phrase.Word) && phrase.Word.Length <= 64 : IsStaticWord(phrase.Word));
    internal static bool IsValid(QuickPhrase phrase, IReadOnlyList<string>? roomNicknames = null) =>
        IsKnownPhrase(phrase) && (!phrase.IsRoomNickname || roomNicknames?.Contains(phrase.Word, StringComparer.Ordinal) == true);

    internal static IReadOnlyList<string> Search(string? category, string query, string[]? roomNicknames = null)
    {
        var words = category switch
        {
            RoomNicknameCategory => roomNicknames ?? [],
            null when roomNicknames is { Length: > 0 } => roomNicknames.Concat(AllWords).Distinct(StringComparer.Ordinal).ToArray(),
            null => AllWords,
            _ => Groups.GetValueOrDefault(category) ?? []
        };
        return Filter(words, query);
    }

    internal static IReadOnlyList<string> SearchTemplates(string? category, string query) =>
        Filter(category == null ? Data.Templates : TemplateGroups.GetValueOrDefault(category) ?? [], query);

    private static IReadOnlyList<string> Filter(string[] candidates, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return candidates;
        // Preserve catalogue order within a rank, but put a whole word or substring before a scattered match.
        return candidates.Select(value => (value, rank: Rank(value, query))).Where(hit => hit.rank >= 0)
            .OrderBy(hit => hit.rank).Select(hit => hit.value).ToArray();
    }

    private static int Rank(string value, string query)
    {
        if (value.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (value.Contains(query, StringComparison.OrdinalIgnoreCase)) return 1;
        int next = 0;
        foreach (char c in value)
            if (char.ToUpperInvariant(c) == char.ToUpperInvariant(query[next]) && ++next == query.Length) return 2;
        return -1;
    }

    internal static IReadOnlyList<QuickPhrase> Remember(IReadOnlyList<QuickPhrase> previous, QuickPhrase sent) =>
        previous.Prepend(sent).DistinctBy(phrase => phrase.Text, StringComparer.Ordinal).Take(5).ToArray();
}
