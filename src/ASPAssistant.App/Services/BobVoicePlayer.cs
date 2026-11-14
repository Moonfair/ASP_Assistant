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
            new("NewGame_03", "嘿，我最喜欢的指挥官回来了！准备来一局吗？", 4441)
        ],
        BobVoiceCue.Upgrade =>
        [
            new("AfterShopUpgrade_01", "太好了！现在你能找到更棒的随从了。", 4052),
            new("AfterShopUpgrade_03", "非常好！现在我能提供更强大的随从了。", 4151)
        ],
        BobVoiceCue.TrackedShopItem =>
        [
            new("Hire_01", "去吧，从这些人选里挑一位。", 2841),
            new("General_02", "我给你物色了一些新人！", 2295)
        ],
        BobVoiceCue.BeforeBattle =>
        [
            new("Ahead_03", "这局我觉得你能赢。", 1977),
            new("Ahead_02", "保持这个气势，朋友！", 1848),
            new("ShopToCombat_02", "我会暗中支持你的——可别告诉别人。", 3224)
        ],
        BobVoiceCue.BattleVictory =>
        [
            new("FirstVictory_01", "这游戏就是这么玩儿的！你还真是个人才啊，朋友！", 5097)
        ],
        BobVoiceCue.BattleLoss =>
        [
            new("CombatLoss_01", "没关系，人生难免不如意。", 3961)
        ],
        BobVoiceCue.LowHealth =>
        [
            new("Behind_01", "你现在还有反败为胜的机会！", 2875)
        ],
        BobVoiceCue.Recruit =>
        [
            new("RecruitSmallMinion_01", "它不是最强的，却是最拼的。", 3050),
            new("RecruitMediumMinion_01", "要是我的话，我也选这个。", 2137)
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
            new("Flavor_06", "你问我怎么招的这些随从？四个字：漏斗蛋糕。", 5979),
            new("Flavor_08", "优质随从们慕名而来：我这儿有好吃的香辣饼干。", 5509),
            new("Flavor_04", "有一次我给死亡骑士倒酒。他一接过去……那杯酒就冻住啦！", 5736)
        ],
        _ => []
    };

    private string PathFor(Clip clip) => Path.Combine(directory, Prefix + clip.Suffix + ".wav");

    internal bool Available => File.Exists(Path.Combine(directory, Prefix + "NewGame_01.wav"));

    internal bool Play(BobVoiceCue cue, bool preview = false)
    {
        var cooldown = cue is BobVoiceCue.BeforeBattle or BobVoiceCue.BattleLoss or BobVoiceCue.BattleVictory
            ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(8);
        if (!preview && _lastPlayback is long last && Stopwatch.GetElapsedTime(last) < cooldown)
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
    }

    public void Dispose()
    {
        Stop();
        _player.Dispose();
    }
}
