using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ASPAssistant.App.Controls;

internal sealed partial record LineupSourceVideo(string Author, string Url)
{
    private const string Opener = "```aspa-source";
    private const string Closer = "```";
    internal const string SklandHost = "game.skland.com";

    internal bool IsEmpty => string.IsNullOrWhiteSpace(Author) && string.IsNullOrWhiteSpace(Url);
    internal bool CanSerializeAuthor => !LineupDiagramSource.ClosesFence(SingleLine(Author), Closer);
    internal bool CanSerializeUrl => !LineupDiagramSource.ClosesFence(SingleLine(Url), Closer);

    private static bool IsSourceFence(LineupDiagramSource.Fence fence)
        => fence.Opening == Opener && fence.Closing == Closer && fence.Lines.Length == 2;

    internal static LineupSourceVideo? ReadFence(LineupDiagramSource.Fence fence)
        => IsSourceFence(fence) ? new LineupSourceVideo(fence.Lines[0], fence.Lines[1]) : null;

    internal static (string Introduction, string Diagram, LineupSourceVideo Video) SplitDescription(string description)
    {
        var fence = LineupDiagramSource.FindFences(description).FirstOrDefault(IsSourceFence);
        var video = new LineupSourceVideo("", "");
        if (fence is not null)
        {
            video = new LineupSourceVideo(fence.Lines[0], fence.Lines[1]);
            var before = description[..fence.Start].TrimEnd('\r', '\n');
            var after = description[fence.End..].TrimStart('\r', '\n');
            description = before.Length == 0 ? after
                : after.Length == 0 ? before
                : before + "\n\n" + after;
        }
        var (introduction, diagram) = LineupDiagramSource.SplitDescription(description);
        return (introduction, diagram, video);
    }

    internal static string CombineDescription(string introduction, string diagram, LineupSourceVideo video)
    {
        if (video.IsEmpty) return LineupDiagramSource.CombineDescription(introduction, diagram);
        var block = string.Join("\n", Opener, SingleLine(video.Author), SingleLine(video.Url), Closer);
        var putSourceFirst = LineupDiagramSource.FindFences(introduction)
            .Any(fence => fence.Closing is null || IsSourceFence(fence));
        var prose = introduction.Length == 0 ? block
            : putSourceFirst ? block + "\n\n" + introduction
            : introduction.TrimEnd('\r', '\n') + "\n\n" + block;
        return LineupDiagramSource.CombineDescription(prose, diagram);
    }

    private static string SingleLine(string text)
        => text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();

    internal Uri? BrowserUri
    {
        get
        {
            var url = Url.Trim();
            if (!VideoUrlPattern().IsMatch(url) && !IsSklandGuideUrl(url)) return null;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
        }
    }

    private static bool IsSklandGuideUrl(string url)
    {
        var match = SklandUrlPattern().Match(url);
        if (!match.Success) return false;

        var hasGuideId = false;
        foreach (var field in match.Groups["query"].Value.Split('&'))
        {
            var separator = field.IndexOf('=');
            var key = separator < 0 ? field : field[..separator];
            if (Uri.UnescapeDataString(key) != "guideId") continue;
            if (hasGuideId || key != "guideId" || separator < 0) return false;
            var value = field[(separator + 1)..];
            if (value.Length == 0 || !value.All(char.IsAsciiDigit)) return false;
            hasGuideId = true;
        }
        return hasGuideId;
    }

    internal bool TryOpen()
    {
        if (BrowserUri is not { } uri) return false;
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception) { return false; }
    }

    [GeneratedRegex(@"\A(?i:https://)(?:(?i:(?:(?:www|m)\.)?bilibili\.com)(?::443)?/video/(?:BV[0-9A-Za-z]+|av[0-9]+)|(?i:b23\.tv)(?::443)?/[0-9A-Za-z]+)/?(?:[?#][^\s\\\p{Cc}]*)?\z")]
    private static partial Regex VideoUrlPattern();

    [GeneratedRegex(@"\A(?i:https://game\.skland\.com)(?::443)?/arknights/stronghold-guide/detail\?(?<query>[^#\s\\\p{Cc}]+)(?:#[^\s\\\p{Cc}]*)?\z")]
    private static partial Regex SklandUrlPattern();
}
