using ASPAssistant.Core.GameModes.GarrisonProtocol;

namespace ASPAssistant.Core.Services;

public enum BobVoiceCue
{
    Opening, Upgrade, TrackedShopItem, BeforeBattle, BattleVictory, BattleLoss,
    LowHealth, Recruit, Sell, PreparationChatter
}

public sealed class BobVoiceTriggers
{
    private readonly record struct ShopFrame(int Round, int Level, int Gold);

    private ShopFrame? _previousFrame;
    private ShopFrame? _confirmed;
    private HashSet<string> _previousNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _announcedNames = new(StringComparer.Ordinal);
    private readonly HashSet<(int Round, BobVoiceCue Cue)> _battleCues = [];
    private int _nameRound;
    private bool _victoryAnnounced;
    private int? _lastRound;
    private bool? _previousHealthWasLow;
    private bool _lowHealthAnnounced;
    private TimeSpan? _nextChatter;

    public BobVoiceCue? Observe(GarrisonHud hud, IEnumerable<string> trackedShopNames,
        GarrisonVoiceFrame voice = default, TimeSpan? elapsed = null)
    {
        if (hud.Round is int round && round > 0)
        {
            if (_lastRound > 1 && round == 1 && (hud is { ShopLevel: 1, Gold: 4 } || voice.Preparation))
                Reset();
            _lastRound = round;
        }

        var shopCue = ObserveShop(hud, trackedShopNames, voice);
        if (hud.Round is > 0 && voice.BattleCue is { } battleCue && _battleCues.Add((hud.Round.Value, battleCue)))
        {
            if (battleCue != BobVoiceCue.BattleVictory) return battleCue;
            if (!_victoryAnnounced)
            {
                _victoryAnnounced = true;
                return battleCue;
            }
        }

        bool? low = voice.Health.HasValue ? voice.Health.Value <= 10 : null;
        bool healthConfirmed = low.HasValue && low == _previousHealthWasLow;
        _previousHealthWasLow = low;
        if (healthConfirmed && low == false) _lowHealthAnnounced = false;
        if (healthConfirmed && low == true && !_lowHealthAnnounced)
        {
            _lowHealthAnnounced = true;
            return BobVoiceCue.LowHealth;
        }

        if (!voice.Preparation || voice.BattleCue.HasValue)
        {
            _nextChatter = null;
            return shopCue;
        }

        if (elapsed is not { } now || hud.Round is not > 0 ||
            _battleCues.Contains((hud.Round.Value, BobVoiceCue.BeforeBattle))) return shopCue;
        _nextChatter ??= now + ChatterDelay();
        if (shopCue.HasValue || now < _nextChatter) return shopCue;

        _nextChatter = now + ChatterDelay();
        return BobVoiceCue.PreparationChatter;
    }

    private BobVoiceCue? ObserveShop(GarrisonHud hud, IEnumerable<string> trackedShopNames, GarrisonVoiceFrame voice)
    {
        if (hud is not { Round: > 0, ShopLevel: > 0, Gold: >= 0 } || voice.BattleCue.HasValue)
        {
            _previousFrame = null;
            _previousNames.Clear();
            return null;
        }

        var current = new ShopFrame(hud.Round!.Value, hud.ShopLevel!.Value, hud.Gold!.Value);
        var names = trackedShopNames.ToHashSet(StringComparer.Ordinal);
        var priorNames = _previousNames;
        _previousNames = names;
        var priorFrame = _previousFrame;
        _previousFrame = current;
        if (priorFrame != current) return null;

        if (_nameRound != current.Round)
        {
            _nameRound = current.Round;
            _announcedNames.Clear();
        }

        var prior = _confirmed;
        if (prior == null || current.Round >= prior.Value.Round && current.Level >= prior.Value.Level)
            _confirmed = current;

        if (current is { Round: 1, Level: 1 } && prior == null)
        {
            _announcedNames.UnionWith(names);
            return BobVoiceCue.Opening;
        }
        if (prior is { } previous && current.Round >= previous.Round && current.Level > previous.Level)
            return BobVoiceCue.Upgrade;
        if (prior is { } baseline && current.Round == baseline.Round && current.Level == baseline.Level)
        {
            int delta = current.Gold - baseline.Gold;
            if (delta < -2) return BobVoiceCue.Recruit;
            if (delta == 1) return BobVoiceCue.Sell;
        }

        bool hasNewName = false;
        foreach (var name in names)
            if (priorNames.Contains(name) && _announcedNames.Add(name))
                hasNewName = true;

        return hasNewName ? BobVoiceCue.TrackedShopItem : null;
    }

    public void RetryLowHealth() => _lowHealthAnnounced = false;

    private static TimeSpan ChatterDelay() => TimeSpan.FromSeconds(Random.Shared.Next(15, 31));

    public void Reset()
    {
        _previousFrame = _confirmed = null;
        _previousNames.Clear();
        _announcedNames.Clear();
        _battleCues.Clear();
        _nameRound = 0;
        _lastRound = null;
        _victoryAnnounced = false;
        _previousHealthWasLow = null;
        _lowHealthAnnounced = false;
        _nextChatter = null;
    }
}
