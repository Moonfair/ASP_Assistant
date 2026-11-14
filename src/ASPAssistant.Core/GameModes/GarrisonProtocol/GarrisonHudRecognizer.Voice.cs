using System.Drawing;
using ASPAssistant.Core.Services;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.GameModes.GarrisonProtocol;

public sealed partial class GarrisonHudRecognizer
{
    private GarrisonVoiceFrame ReadVoiceFrame(MaaImageBuffer frame, Bitmap bitmap, Rectangle bounds)
    {
        int center = bounds.Width / 2;
        int right = bounds.Right;
        if (recognition.FindHudTemplate(frame, new Rectangle(center - 146, 12, 82, 37), .85, ["voice-orange-theme"]) != null)
            return ReadOrangeVoiceFrame(frame, bitmap, center, right);
        if (HasClosureRoundBadge(bitmap, center))
            return ReadClosureVoiceFrame(frame, bitmap, center, right);

        var resultArea = new Rectangle(center - 240, 288, 212, 122);
        bool victory = recognition.FindHudTemplate(frame, resultArea, .85, ["voice-victory"]) != null;
        bool loss = !victory && recognition.FindHudTemplate(frame, resultArea, .85, ["voice-loss"]) != null;
        bool preparation = !victory && !loss &&
            recognition.FindHudTemplate(frame, new Rectangle(center - 86, 8, 168, 44), .83, ["voice-rest", "voice-rest-modern"]) != null;

        BobVoiceCue? cue = victory ? BobVoiceCue.BattleVictory : loss ? BobVoiceCue.BattleLoss : null;
        if (preparation && IsReadyPanelVisible(bitmap, right) &&
            (HasRedCountdown(bitmap, right) || HasExactlyOneUnreadyPlayer(bitmap, right)))
            cue = BobVoiceCue.BeforeBattle;

        var healthArea = new Rectangle(center + 112, 15, 54, 37);
        var healthAnchor = recognition.FindHudTemplate(frame, healthArea, .78, ["voice-health", "voice-health-modern"]);
        int? health = healthAnchor is { } anchor
            ? ReadNumber(frame, bounds, anchor, new Rectangle(13, -1, 26, 21), GoldNumber)
            : null;

        return new GarrisonVoiceFrame(health, preparation, cue);
    }

    private GarrisonVoiceFrame ReadOrangeVoiceFrame(MaaImageBuffer frame, Bitmap bitmap, int center, int right)
    {
        // Match the result subtitles so the portrait and the variable damage number do not influence the outcome.
        var resultArea = new Rectangle(center - 29, 382, 148, 44);
        if (recognition.FindHudTemplate(frame, resultArea, .87, ["voice-victory-orange"]) != null)
            return new GarrisonVoiceFrame(BattleCue: BobVoiceCue.BattleVictory);
        if (recognition.FindHudTemplate(frame, resultArea, .87, ["voice-loss-orange"]) != null)
            return new GarrisonVoiceFrame(BattleCue: BobVoiceCue.BattleLoss);

        var restArea = new Rectangle(center - 86, 8, 168, 44);
        if (recognition.FindHudTemplate(frame, restArea, .83, ["voice-rest-modern"]) == null)
            return default;

        var buttonArea = new Rectangle(right - 156, 94, 126, 45);
        bool hasReadyPanel = recognition.FindHudTemplate(frame, buttonArea, .84,
            ["voice-orange-ready-button", "voice-orange-cancel-ready"]) != null;
        bool imminent = hasReadyPanel && (HasOrangeRedCountdown(bitmap, right) || HasOneOrangeUnreadyPlayer(bitmap, right));
        return new GarrisonVoiceFrame(Preparation: true, BattleCue: imminent ? BobVoiceCue.BeforeBattle : null);
    }

    private static bool HasOrangeRedCountdown(Bitmap bitmap, int right)
    {
        // The low-time flash tints the blue ornament around the timer, independent of the displayed digits.
        int red = 0;
        for (int y = 12; y <= 59; y += 6)
        for (int x = right - 40; x <= right - 5; x += 5)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.R >= 80 && color.R - color.G >= 60 && color.R - color.B >= 60)
                red++;
        }
        return red >= 34;
    }

    private static bool HasOneOrangeUnreadyPlayer(Bitmap bitmap, int right)
    {
        // The bar is paler and slightly higher than the older themes. Read below the keyboard hint.
        int ready = 0;
        int unready = 0;
        foreach (int offset in new[] { 115, 95, 76, 57 })
        {
            int lit = 0;
            int dim = 0;
            for (int y = 165; y <= 173; y += 2)
            for (int x = right - offset - 2; x <= right - offset + 2; x += 2)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.G >= 140 && color.G - color.R >= 15 && color.B - color.R >= 8) lit++;
                if (color.R is >= 45 and <= 101 && Math.Abs(color.R - color.G) <= 10 &&
                    Math.Abs(color.R - color.B) <= 15) dim++;
            }
            if (lit >= 8) ready++;
            else if (dim >= 8) unready++;
        }
        return ready == 3 && unready == 1;
    }

    private static bool HasClosureRoundBadge(Bitmap bitmap, int center)
    {
        // Sample both ends of the cyan badge, leaving out the changing round number.
        int cyan = 0;
        foreach (int x in new[] { center - 152, center - 130 })
        foreach (int y in new[] { 28, 33, 38 })
        {
            var color = bitmap.GetPixel(x, y);
            if (color.G > 165 && color.B > 175 && color.G - color.R > 100) cyan++;
        }
        return cyan >= 4;
    }

    private GarrisonVoiceFrame ReadClosureVoiceFrame(MaaImageBuffer frame, Bitmap bitmap, int center, int right)
    {
        var resultArea = new Rectangle(center + 38, 363, 142, 49);
        if (recognition.FindHudTemplate(frame, resultArea, .87, ["voice-victory-closure"]) != null)
            return new GarrisonVoiceFrame(BattleCue: BobVoiceCue.BattleVictory);
        if (recognition.FindHudTemplate(frame, resultArea, .87, ["voice-loss-closure"]) != null)
            return new GarrisonVoiceFrame(BattleCue: BobVoiceCue.BattleLoss);

        var restArea = new Rectangle(center - 86, 2, 168, 55);
        if (recognition.FindHudTemplate(frame, restArea, .83, ["voice-rest-closure"]) == null)
            return default;

        var buttonArea = new Rectangle(right - 156, 92, 134, 48);
        bool hasReadyPanel = recognition.FindHudTemplate(frame, buttonArea, .84,
            ["voice-closure-ready-button", "voice-closure-cancel-ready"]) != null;
        bool imminent = hasReadyPanel && (HasClosureCountdownWarning(bitmap, right) ||
            HasExactlyOneUnreadyPlayer(bitmap, right, [119, 99, 80, 60], top: 162));
        return new GarrisonVoiceFrame(Preparation: true, BattleCue: imminent ? BobVoiceCue.BeforeBattle : null);
    }

    private static bool HasClosureCountdownWarning(Bitmap bitmap, int right)
    {
        // Sample the interior of the CRT, whose blue face turns orange regardless of its digits.
        int orange = 0;
        for (int y = 41; y <= 81; y += 5)
        for (int x = right - 135; x <= right - 70; x += 5)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.R > 165 && color.R - color.G > 23 && color.G - color.B > 36)
                orange++;
        }
        return orange >= 45;
    }

    private static bool IsReadyPanelVisible(Bitmap bitmap, int right)
    {
        // Both "准备" and "取消准备" use the same wide light button below the countdown.
        int lit = 0;
        foreach (int x in new[] { right - 153, right - 50, right - 33 })
        foreach (int y in new[] { 101, 127 })
        {
            var color = bitmap.GetPixel(x, y);
            if (color.R > 110 && color.G > 110 && color.B > 110 && Math.Abs(color.R - color.B) < 65)
                lit++;
        }
        return lit >= 4;
    }

    private static bool HasRedCountdown(Bitmap bitmap, int right)
    {
        int red = 0;
        for (int y = 16; y <= 72; y += 8)
        for (int x = right - 53; x <= right - 9; x += 8)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.R >= 105 && color.R - color.G >= 64 && color.R - color.B >= 65)
                red++;
        }
        return red >= 23;
    }

    private static bool HasExactlyOneUnreadyPlayer(Bitmap bitmap, int right) =>
        HasExactlyOneUnreadyPlayer(bitmap, right, [121, 101, 82, 62], top: 164);

    private static bool HasExactlyOneUnreadyPlayer(Bitmap bitmap, int right, ReadOnlySpan<int> offsets, int top)
    {
        int ready = 0;
        int unready = 0;
        // Read the lower halves so the keyboard hint over the heads does not obscure the count.
        foreach (int offset in offsets)
        {
            int teal = 0;
            int gray = 0;
            for (int y = top; y < top + 14; y += 3)
            for (int x = right - offset - 3; x <= right - offset + 3; x += 3)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.G > 140 && color.G - color.R > 90 && color.B - color.R > 60) teal++;
                if (color is { R: >= 47 and <= 113, G: >= 38 and <= 105, B: >= 35 and <= 98 } &&
                    color.R - color.B is >= -5 and <= 29) gray++;
            }
            if (teal >= 9) ready++;
            else if (gray >= 10) unready++;
        }

        return ready == 3 && unready == 1;
    }
}
