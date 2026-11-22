using System.Diagnostics;
using System.IO;
using System.Media;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Services;

internal sealed class BobVoicePlayer(string directory) : IDisposable
{
    private const string Prefix = "VO_DALA_BOSS_99h_Male_Human_";
    private readonly SoundPlayer _player = new();
    private readonly Dictionary<BobVoiceCue, string> _previousClip = [];
    private long? _lastPlayback;
    private TimeSpan _lastClipDuration;

    internal event Action<string, TimeSpan>? Spoken;

    internal readonly record struct Clip(string Suffix, string Caption, int DurationMilliseconds)
    {
        internal TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMilliseconds);
    }

    internal static IReadOnlyList<Clip> GetClips(BobVoiceCue cue) => cue switch
    {
        BobVoiceCue.Opening =>
        [
            new("NewGame_01", "这次你会采取什么策略呢，朋友？", 2873),
            new("NewGame_03", "嘿，我最喜欢的指挥官回来了！准备来一局吗？", 4441),
            new("NewModeLaunch_01", "棋盘准备好了！相信你一定能大获全胜，我的朋友。", 5593),
            new("General_01", "欢迎回来！最近过得怎么样？", 3071)
        ],
        BobVoiceCue.Upgrade =>
        [
            new("AfterShopUpgrade_02", "很好，准备开始游戏吧！", 2818),
            new("AfterShopUpgrade_04", "好了，重新开张！", 2439)
        ],
        BobVoiceCue.TrackedShopItem =>
        [
            new("Hire_01", "去吧，从这些人选里挑一位。", 2841),
            new("General_02", "我给你物色了一些新人！", 2295),
            new("AfterTriple_02", "这就是你要找的那一个。", 2086)
        ],
        BobVoiceCue.ShopRefreshed =>
        [
            new("Hire_01", "去吧，从这些人选里挑一位。", 2841),
            new("General_02", "我给你物色了一些新人！", 2295)
        ],
        BobVoiceCue.ShopFrozen =>
        [
            new("AfterFreezing_01", "下次的时候它们还会在这儿。", 2437),
            new("AfterFreezing_02", "我会暂时把它们保存起来。", 2529)
        ],
        BobVoiceCue.Promotion =>
        [
            new("AfterTriple_01", "哦吼！你又前进了一大步！", 3284),
            new("AfterTriple_03", "绝妙的操作。看看接下来会发生什么。", 3941)
        ],
        BobVoiceCue.TripleOpportunity =>
        [
            new("Triple_01", "选那个随从，你就获得了一个三连。", 3403)
        ],
        BobVoiceCue.BeforeBattle =>
        [
            new("Ahead_03", "这局我觉得你能赢。", 1977),
            new("Ahead_02", "保持这个气势，朋友！", 1848),
            new("ShopToCombat_02", "我会暗中支持你的——可别告诉别人。", 3224),
            new("ShopToCombat_01", "享受战斗吧，朋友！", 2215),
            new("Flavor_05", "我有个预感，我觉得这次你能赢。", 4187)
        ],
        BobVoiceCue.FirstVictory =>
        [
            new("FirstVictory_01", "这游戏就是这么玩儿的！你还真是个人才啊，朋友！", 5097)
        ],
        BobVoiceCue.BattleVictory =>
        [
            new("CombatWin_02", "你干得不错！", 1472),
            new("CombatWin_03", "干得漂亮！保持状态。", 2867),
            new("Ahead_01", "你的表现真不错！", 1867),
            new("FirstPlace_03", "看看你能否保持优势！", 2456)
        ],
        BobVoiceCue.BattleStart =>
        [
            new("General_03", "战斗正在逐渐升温！", 2255)
        ],
        BobVoiceCue.BattleLoss =>
        [
            new("CombatLoss_01", "没关系，人生难免不如意。", 3961),
            new("CombatLoss_02", "战斗过于艰难么？重整旗鼓，再来一局！", 4531),
            new("CombatLoss_03", "每一场失败都会让你的心智更加强大！", 3907)
        ],
        BobVoiceCue.LowHealth =>
        [
            new("Behind_01", "你现在还有反败为胜的机会！", 2875),
            new("Behind_02", "形势还不算太糟糕！", 2168),
            new("Behind_03", "你还有翻盘的可能！", 2082),
            new("AFK_01", "现在你可能有点落后，但你肯定能慢慢赶上来的。", 4949)
        ],
        BobVoiceCue.Recruit =>
        [
            new("RecruitSmallMinion_01", "它不是最强的，却是最拼的。", 3050),
            new("RecruitMediumMinion_01", "要是我的话，我也选这个。", 2137),
            new("RecruitSmallMinion_02", "大小搭配，战斗不累。", 2680),
            new("RecruitSmallMinion_03", "这个小家伙也可能改写战局。", 2687),
            new("RecruitMediumMinion_02", "优秀的战术。", 1687),
            new("RecruitMediumMinion_03", "我喜欢这个选择。", 2033),
            new("RecruitLargeMinion_02", "你这支队伍越来越强了，朋友！", 2886),
            new("RecruitLargeMinion_01", "那家伙破坏力惊人！", 2035)
        ],
        BobVoiceCue.DraftChoice =>
        [
            new("RecruitLargeMinion_03", "座右铭：不选大的怎么能赢？", 3352)
        ],
        BobVoiceCue.Sell =>
        [
            new("AfterSelling_02", "我也不喜欢那家伙。拿好你的钱。", 3973)
        ],
        BobVoiceCue.PreparationChatter =>
        [
            new("Hire_02", "能招人的时候要尽量招揽。", 2915),
            new("Idle_02", "花钱要慎重。但……你也得花呀！", 3987),
            new("Idle_03", "看你打牌是种享受。", 2667),
            new("Idle_01", "看来这是个艰难的决定。", 3813),
            new("Idle_04", "你会如何施展你的谋略呢。", 2561),
            new("Idle_05", "看看你如何应对这个局面。", 2475)
        ],
        _ => []
    };

    private string PathFor(Clip clip) => Path.Combine(directory, Prefix + clip.Suffix + ".wav");

    internal bool Available => File.Exists(Path.Combine(directory, Prefix + "NewGame_01.wav"));

    internal static bool IsCoolingDown(BobVoiceCue cue, TimeSpan elapsed, TimeSpan previousDuration)
    {
        var seconds = cue switch
        {
            BobVoiceCue.BeforeBattle or BobVoiceCue.BattleLoss or BobVoiceCue.BattleVictory or BobVoiceCue.FirstVictory or BobVoiceCue.BattleStart => 1,
            BobVoiceCue.PreparationChatter => Math.Max(5, previousDuration.TotalSeconds),
            _ => 8
        };
        return elapsed < TimeSpan.FromSeconds(seconds);
    }

    internal bool Play(BobVoiceCue cue, bool preview = false)
    {
        if (!preview && _lastPlayback is long last && IsCoolingDown(cue, Stopwatch.GetElapsedTime(last), _lastClipDuration))
            return false;

        var clips = GetClips(cue).Where(clip => File.Exists(PathFor(clip))).ToArray();
        if (clips.Length == 0) return false;

        var choices = clips.Length > 1 && _previousClip.TryGetValue(cue, out var previous)
            ? clips.Where(clip => clip.Suffix != previous).ToArray() : clips;
        var chosen = choices[Random.Shared.Next(choices.Length)];
        try
        {
            _player.SoundLocation = PathFor(chosen);
            _player.Play();
            _lastPlayback = Stopwatch.GetTimestamp();
            _lastClipDuration = chosen.Duration;
            _previousClip[cue] = chosen.Suffix;
            AppLogger.Info("BobVoice", $"Playing {cue}: {Path.GetFileName(_player.SoundLocation)}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLogger.Error("BobVoice", "WAV playback failed", ex);
            return false;
        }

        Spoken?.Invoke(chosen.Caption, chosen.Duration);
        return true;
    }

    internal void Stop()
    {
        _player.Stop();
        _lastPlayback = null;
        _lastClipDuration = TimeSpan.Zero;
    }

    public void Dispose()
    {
        Stop();
        _player.Dispose();
    }
}
