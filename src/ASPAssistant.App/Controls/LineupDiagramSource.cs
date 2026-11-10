using System.Text;
using System.Text.RegularExpressions;

namespace ASPAssistant.App.Controls;

internal static partial class LineupDiagramSource
{
    internal sealed record Block(int Start, int End, string Kind, string[] Lines, bool Closed);

    internal static List<Block> FindBlocks(string source, out bool hasUnclosedFence)
    {
        hasUnclosedFence = false;
        var result = new List<Block>();
        var lines = LinePattern().Matches(source);
        for (var i = 0; i < lines.Count; i++)
        {
            var opener = lines[i].Groups[1].Value.Trim();
            if (!opener.StartsWith("```", StringComparison.Ordinal)) continue;
            var start = lines[i].Index;
            var kind = opener[3..].Trim();
            var body = new List<string>();
            while (++i < lines.Count && lines[i].Groups[1].Value.Trim() != "```")
                body.Add(lines[i].Groups[1].Value);
            var closed = i < lines.Count;
            if (!closed) hasUnclosedFence = true;
            if (kind is not ("lineup" or "mermaid")) continue;
            var end = closed ? lines[i].Index + lines[i].Groups[1].Length : source.Length;
            result.Add(new Block(start, end, kind, body.ToArray(), closed));
        }
        return result;
    }

    internal static string Format(LineupDiagram.Graph graph, string kind, string newline)
        => string.Join(newline, new[] { "```" + kind }.Concat(graph.ToLines()).Append("```"));

    internal static List<Block> FindEditorBlocks(string source, out bool hasUnclosedFence)
    {
        var blocks = FindBlocks(source, out hasUnclosedFence);
        if (blocks.Count == 0 && IsRawDiagram(source))
        {
            var lines = LinePattern().Matches(source).Select(match => match.Groups[1].Value).ToArray();
            blocks.Add(new Block(0, source.Length, "lineup", lines, Closed: true));
        }
        return blocks;
    }

    internal static (string Introduction, string DiagramSource) SplitDescription(string description)
    {
        var blocks = FindBlocks(description, out _);
        if (blocks.Count == 0) return (description, "");
        var prose = new StringBuilder();
        var cursor = 0;
        foreach (var block in blocks)
        {
            prose.Append(description, cursor, block.Start - cursor);
            cursor = block.End;
        }
        prose.Append(description, cursor, description.Length - cursor);
        return (prose.ToString().Trim('\r', '\n'),
            string.Join("\n\n", blocks.Select(block => description[block.Start..block.End])));
    }

    internal static string CombineDescription(string introduction, string diagramSource)
    {
        if (string.IsNullOrWhiteSpace(diagramSource)) return introduction;
        var code = diagramSource.Trim('\r', '\n');
        if (IsRawDiagram(code)) code = "```lineup\n" + code + "\n```";
        return introduction.Length == 0 ? code : introduction.TrimEnd('\r', '\n') + "\n\n" + code;
    }

    private static bool IsRawDiagram(string source)
        => !string.IsNullOrWhiteSpace(source)
            && !LinePattern().Matches(source).Any(match => match.Groups[1].Value.TrimStart().StartsWith("```", StringComparison.Ordinal));

    internal static bool IsLegacyPositionLine(string line)
    {
        const string marker = "%% asp-pos";
        return line.StartsWith(marker, StringComparison.Ordinal)
            && (line.Length == marker.Length || char.IsWhiteSpace(line[marker.Length]));
    }

    internal static string RemoveLegacyPositions(string source)
    {
        StringBuilder? result = null;
        var start = 0;
        foreach (var block in FindBlocks(source, out _))
        {
            for (var line = LinePattern().Match(source, block.Start); line.Success && line.Index < block.End; line = line.NextMatch())
            {
                if (!IsLegacyPositionLine(line.Groups[1].Value.Trim())) continue;
                result ??= new StringBuilder(source.Length);
                result.Append(source, start, line.Index - start);
                start = line.Index + line.Length;
            }
        }
        return result is null ? source : result.Append(source, start, source.Length - start).ToString();
    }

    [GeneratedRegex(@"([^\r\n]*)(?:\r\n|\r|\n|\z)")]
    private static partial Regex LinePattern();
}
