using System.Text.Encodings.Web;
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed class LineupShareService(IEnumerable<string> operators, IEnumerable<string> equipment,
    IReadOnlyDictionary<string, int>? equipmentSlotCounts = null, OperatorCatalog? operatorCatalog = null)
{
    public const int MaxShareTextLength = 100_000;
    private const string LegacyHeader = "ASPA-LINEUP/1";
    private const string Header = "ASPA-LINEUP/2";
    private const string ShareComment = "# ASP Assistant · 明日方舟「卫戍协议」实时辅助工具 · 官网 https://aspa.moonfair.cn";
    private readonly HashSet<string> _operators = new(operators, StringComparer.Ordinal);
    private readonly HashSet<string> _equipment = new(equipment, StringComparer.Ordinal);

    public static LineupShareService FromCatalog(IReadOnlyList<Operator> operators, IEnumerable<string> equipment,
        OperatorCatalog? operatorCatalog = null) =>
        new(operators.Select(op => op.Name), equipment, operators.Where(op => op.EquipmentSlotCount != 2)
            .ToDictionary(op => op.Name, op => op.EquipmentSlotCount, StringComparer.Ordinal), operatorCatalog);
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
        var tags = lineup.CovenantTags is { Count: > 0 } ? lineup.CovenantTags : null;
        var usesExtraSlots = lineup.Members.Any(member => member.Equipment3 is not null || member.Equipment4 is not null);
        var hasEliteMembers = lineup.Members.Any(member => member.IsElite);
        var header = description is null && strategies is null && tags is null && !usesExtraSlots && !hasEliteMembers
            ? LegacyHeader : Header;
        return header + "\n" + ShareComment + "\n" +
            JsonSerializer.Serialize(new ShareContent(lineup.Name, lineup.Author, lineup.Members, description, strategies, tags), JsonOptions);
    }

    public Lineup Import(string text)
    {
        var lineup = Parse(text);
        Validate(lineup);
        return lineup;
    }

    public async Task<Lineup> ImportAsync(string text)
    {
        var lineup = Parse(text);
        ValidateStructure(lineup);
        if (operatorCatalog is not null)
            await operatorCatalog.LoadForNamesAsync(lineup.Members.Select(member => member.OperatorName));
        ValidateCatalog(lineup);
        return lineup;
    }

    private static Lineup Parse(string text)
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
                AvailableStrategies = content.AvailableStrategies ?? [], CovenantTags = content.CovenantTags
            };
            return lineup;
        }
        catch (JsonException ex) { throw new FormatException("阵容文本不完整或字段格式错误。", ex); }
    }

    public void Validate(Lineup lineup)
    {
        ValidateStructure(lineup);
        ValidateCatalog(lineup);
    }

    private void ValidateCatalog(Lineup lineup)
    {
        foreach (var member in lineup.Members)
        {
            if (!_operators.Contains(member.OperatorName) && operatorCatalog?.Find(member.OperatorName) is null)
                throw new FormatException($"当前数据中没有干员“{member.OperatorName}”，请检查赛季或更新数据。");
            var slots = member.EquipmentSlots;
            var slotCount = equipmentSlotCounts?.GetValueOrDefault(member.OperatorName, 2) ?? 2;
            if (slots.Skip(slotCount).Any(name => name.Length > 0))
                throw new FormatException($"干员“{member.OperatorName}”最多可携带 {slotCount} 件装备。");
            foreach (var name in slots)
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
        if (lineup.Members is null || lineup.Members.Count is < 1 or > Lineup.MaxMembers)
            throw new FormatException($"每个阵容需要 1–{Lineup.MaxMembers} 名干员。");
        if (lineup.AvailableStrategies is null || lineup.AvailableStrategies.Count > 100 ||
            lineup.AvailableStrategies.Any(name => !ValidName(name, 100)) ||
            lineup.AvailableStrategies.Distinct(StringComparer.Ordinal).Count() != lineup.AvailableStrategies.Count)
            throw new FormatException("可用策略需为有效且不重复的名称，最多 100 项。");
        if (lineup.CovenantTags is { } tags && (tags.Count > 100 ||
            tags.Any(tag => !ValidName(tag, 100)) || tags.Distinct(StringComparer.Ordinal).Count() != tags.Count))
            throw new FormatException("盟约标签需为有效且不重复的名称，最多 100 项。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in lineup.Members)
        {
            if (member is null || !ValidName(member.OperatorName, 100)) throw new FormatException("阵容中存在无效干员。");
            if (!names.Add(member.OperatorName)) throw new FormatException($"干员“{member.OperatorName}”重复加入了阵容。");
            foreach (var name in member.EquipmentSlots)
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
        List<string>? AvailableStrategies = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        List<string>? CovenantTags = null);
}
