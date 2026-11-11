using System.Text.Encodings.Web;
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed class LineupShareService(IEnumerable<string> operators, IEnumerable<string> equipment)
{
    public const int MaxShareTextLength = 100_000;
    private const string LegacyHeader = "ASPA-LINEUP/1";
    private const string Header = "ASPA-LINEUP/2";
    private const string ShareComment = "# ASP Assistant · 明日方舟「卫戍协议」实时辅助工具 · 官网 https://aspa.moonfair.cn";
    private readonly HashSet<string> _operators = new(operators, StringComparer.Ordinal);
    private readonly HashSet<string> _equipment = new(equipment, StringComparer.Ordinal);
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 16,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public string Export(Lineup lineup)
    {
        Validate(lineup);
        // The share carries content and attribution, never the local identity.
        var description = lineup.Description.Length == 0 ? null : lineup.Description;
        var strategies = lineup.AvailableStrategies.Count == 0 ? null : lineup.AvailableStrategies;
        return (description is null && strategies is null ? LegacyHeader : Header) + "\n" + ShareComment + "\n" +
            JsonSerializer.Serialize(new ShareContent(lineup.Name, lineup.Author, lineup.Members, description, strategies), JsonOptions);
    }

    public Lineup Import(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("请先复制阵容分享文本。");
        if (text.Length > MaxShareTextLength) throw new FormatException("分享文本过长，请检查是否复制了完整且正确的阵容。");
        text = text.Trim().TrimStart('\uFEFF');
        var newline = text.IndexOf('\n');
        if (newline < 0 || text[..newline].TrimEnd('\r') is not (Header or LegacyHeader))
            throw new FormatException("不支持的阵容格式或版本，请使用 ASP Assistant 导出的文本。");
        var payloadStart = newline + 1;
        if (text.AsSpan(payloadStart).StartsWith("# ", StringComparison.Ordinal))
        {
            var commentEnd = text.IndexOf('\n', payloadStart);
            payloadStart = commentEnd < 0 ? text.Length : commentEnd + 1;
        }
        try
        {
            var content = JsonSerializer.Deserialize<ShareContent>(text[payloadStart..], JsonOptions)
                ?? throw new FormatException("分享文本中没有阵容。");
            var lineup = new Lineup
            {
                Name = content.Name, Author = content.Author, Members = content.Members,
                Description = content.Description ?? "", IsImported = true,
                AvailableStrategies = content.AvailableStrategies ?? []
            };
            Validate(lineup);
            return lineup;
        }
        catch (JsonException ex) { throw new FormatException("阵容文本不完整或字段格式错误。", ex); }
    }

    public void Validate(Lineup lineup)
    {
        ValidateStructure(lineup);
        foreach (var member in lineup.Members)
        {
            if (!_operators.Contains(member.OperatorName))
                throw new FormatException($"当前数据中没有干员“{member.OperatorName}”，请检查赛季或更新数据。");
            foreach (var name in new[] { member.Equipment1, member.Equipment2 })
                if (name.Length > 0 && !_equipment.Contains(name))
                    throw new FormatException($"当前数据中没有装备“{name}”，请检查赛季或更新数据。");
        }
    }

    internal static void ValidateStructure(Lineup lineup)
    {
        if (lineup is null || lineup.Id == Guid.Empty) throw new FormatException("无效的阵容标识。");
        if (!ValidName(lineup.Name, 80)) throw new FormatException("请填写阵容名称（1–80 字）。");
        if (!ValidName(lineup.Author, 80)) throw new FormatException("请填写作者名称（1–80 字）。");
        if (lineup.Description is null || lineup.Description.Length > Lineup.MaxDescriptionLength)
            throw new FormatException($"阵容介绍最多支持 {Lineup.MaxDescriptionLength} 字。");
        if (lineup.Members is null || lineup.Members.Count is < 1 or > 10)
            throw new FormatException("每个阵容需要 1–10 名干员。");
        if (lineup.AvailableStrategies is null || lineup.AvailableStrategies.Count > 100 ||
            lineup.AvailableStrategies.Any(name => !ValidName(name, 100)) ||
            lineup.AvailableStrategies.Distinct(StringComparer.Ordinal).Count() != lineup.AvailableStrategies.Count)
            throw new FormatException("可用策略需为有效且不重复的名称，最多 100 项。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in lineup.Members)
        {
            if (member is null || !ValidName(member.OperatorName, 100)) throw new FormatException("阵容中存在无效干员。");
            if (!names.Add(member.OperatorName)) throw new FormatException($"干员“{member.OperatorName}”重复加入了阵容。");
            foreach (var name in new[] { member.Equipment1, member.Equipment2 })
                if (name is null || (name.Length > 0 && !ValidName(name, 100)))
                    throw new FormatException("装备名称无效。");
        }
    }

    private static bool ValidName(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);

    private sealed record ShareContent(string Name, string Author, List<LineupMember> Members,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? Description = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        List<string>? AvailableStrategies = null);
}
