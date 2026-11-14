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

    private static bool HasExactlyOneUnreadyPlayer(Bitmap bitmap, int right)
    {
        int ready = 0;
        int unready = 0;
        // Read the lower halves so the keyboard hint over the heads does not obscure the count.
        foreach (int offset in new[] { 121, 101, 82, 62 })
        {
            int teal = 0;
            int gray = 0;
            for (int y = 164; y <= 177; y += 3)
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
