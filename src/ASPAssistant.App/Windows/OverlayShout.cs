using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ASPAssistant.App.Services;

namespace ASPAssistant.App.Windows;

// Draw into the existing hit-transparent room layer; receiving a callout never opens a
// window, takes focus, or changes the game's mouse/keyboard input mode.
internal static class OverlayShout
{
    internal const int DurationMs = 6000;

    internal static MatchShout? Latest(MatchSnapshot state, long now) =>
        state.Phase == "playing" ? state.Shouts.LastOrDefault(s => s.PlayerId != state.SelfId &&
            s.SentAt > now - DurationMs && s.SentAt <= now && !state.Muted.Contains(s.PlayerId)) : null;

    internal static void Draw(DrawingContext dc, Size size, double dpi, MatchSnapshot state, MatchShout shout)
    {
        var nickname = state.Members.FirstOrDefault(m => m.Id == shout.PlayerId)?.Nickname ?? "队友";
        var face = new Typeface("Microsoft YaHei UI");
        var max = Math.Max(64, Math.Min(380, size.Width * .55));
        FormattedText Line(string value, double em, Brush brush) => new(value, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, face, em, brush, dpi) { MaxTextWidth = max, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        var label = Line($"队伍  /  {nickname}", 12, Brushes.LightSkyBlue);
        var phrase = Line(shout.Text, 16, Brushes.White);
        var width = Math.Min(max, Math.Max(phrase.Width, label.Width)) + 24;
        var left = (size.Width - width) / 2;
        var top = Math.Max(6, size.Height * .07);
        dc.DrawRoundedRectangle(Background, null, new Rect(left, top, width, label.Height + phrase.Height + 16), 5, 5);
        dc.DrawText(label, new(left + 12, top + 6));
        dc.DrawText(phrase, new(left + 12, top + 8 + label.Height));
    }

    private static readonly SolidColorBrush Background = MakeBackground();
    private static SolidColorBrush MakeBackground()
    {
        var brush = new SolidColorBrush(Color.FromArgb(235, 25, 30, 45));
        brush.Freeze();
        return brush;
    }
}
