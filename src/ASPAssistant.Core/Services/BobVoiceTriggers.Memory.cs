using ASPAssistant.Core.GameModes.GarrisonProtocol;

namespace ASPAssistant.Core.Services;

public sealed partial class BobVoiceTriggers
{
    private GarrisonSnapshot? _memoryCandidate;
    private GarrisonSnapshot? _memoryConfirmed;
    private (GameTargetKind Source, int Round)? _lastMemoryBattle;
    private (string? Activity, int Player, int Round)? _tripleContext;
    private readonly HashSet<string> _announcedTriples = new(StringComparer.Ordinal);
    private readonly HashSet<string> _newTriples = new(StringComparer.Ordinal);

    private BobVoiceCue? ObserveBattleStart(BattleUpdate? update, int? round)
    {
        if (round is not > 0 || update is not { State: BattleFeedState.Live, Sample: { } battle })
            return null;
        var identity = (update.Source, battle.Round);
        if (_lastMemoryBattle == identity) return null;
        _lastMemoryBattle = identity;
        if (battle.Ended || battle.GameSeconds is < 0 or > 3 ||
            _battleCues.Contains((round.Value, BobVoiceCue.BeforeBattle)) ||
            !_battleCues.Add((round.Value, BobVoiceCue.BattleStart)))
            return null;
        return BobVoiceCue.BattleStart;
    }

    private BobVoiceCue? ObserveMemoryShop(GarrisonSnapshot? snapshot, GarrisonVoiceFrame voice)
    {
        _newTriples.Clear();
        if (snapshot?.Shop is not { Level: > 0, Coins: >= 0, Offers: { } currentOffers } ||
            voice.BattleCue.HasValue || !(voice.Preparation || voice.ShopVisible == true))
        {
            _memoryCandidate = _memoryConfirmed = null;
            return null;
        }

        var candidate = _memoryCandidate;
        _memoryCandidate = snapshot;
        if (candidate == null || !SameMemoryState(candidate, snapshot)) return null;
        var previous = _memoryConfirmed;
        _memoryConfirmed = snapshot;
        if (candidate.Characters != null && snapshot.Characters != null)
            UpdateTripleOpportunities(snapshot, snapshot.Characters, currentOffers);
        if (previous == null || previous.Round != snapshot.Round ||
            previous.PlayerIndex != snapshot.PlayerIndex || previous.ActivityId != snapshot.ActivityId)
            return null;

        var shop = snapshot.Shop;
        var oldShop = previous.Shop!;
        var oldCharacters = previous.Characters;
        var characters = snapshot.Characters;
        int goldChange = shop.Coins - oldShop.Coins;
        if (oldCharacters != null && characters != null)
        {
            var removed = oldCharacters.Where(old => !characters.Any(c =>
                c.InstanceId == old.InstanceId && c.ChessId == old.ChessId)).ToArray();
            var added = characters.Where(c => !oldCharacters.Any(old =>
                c.InstanceId == old.InstanceId && c.ChessId == old.ChessId)).ToArray();
            foreach (var elite in added.Where(c => c.ChessId.StartsWith("chess_char_", StringComparison.Ordinal) &&
                c.ChessId.EndsWith("_b", StringComparison.Ordinal)))
            {
                var normal = elite.ChessId[..^1] + "a";
                if (removed.Any(c => c.InstanceId == elite.InstanceId && c.ChessId == normal) ||
                    removed.Count(c => c.ChessId == normal) >= 2)
                    return BobVoiceCue.Promotion;
            }
            if (shop.Level == oldShop.Level)
            {
                if (goldChange <= 0 && added.Any(c => oldShop.Offers!.Any(o => o.ChessId == c.ChessId &&
                    (goldChange == -o.Price ||
                        !shop.Offers!.Any(current => current.Slot == o.Slot && current.ChessId == o.ChessId)))))
                    return BobVoiceCue.Recruit;
                if (removed.Length > 0 && added.Length == 0 && goldChange > 0)
                    return BobVoiceCue.Sell;
            }
        }

        if (shop.Offers!.Any(current => current.ChessId.Length > 0 && current.Frozen && oldShop.Offers!.Any(old =>
            old.Slot == current.Slot && old.ChessId == current.ChessId && !old.Frozen)))
            return BobVoiceCue.ShopFrozen;

        var offers = shop.Offers!.Where(o => o.ChessId.Length > 0).OrderBy(o => o.Slot).ToArray();
        var oldOffers = oldShop.Offers!.Where(o => o.ChessId.Length > 0).OrderBy(o => o.Slot).ToArray();
        bool inventoryUnchanged = previous.Characters != null && snapshot.Characters != null &&
            previous.Equipment != null && snapshot.Equipment != null && previous.Magic != null && snapshot.Magic != null &&
            SameInventory(previous, snapshot);
        bool replacedOffers = inventoryUnchanged && offers.Length > 0 &&
            offers.Select(o => o.Slot).SequenceEqual(oldOffers.Select(o => o.Slot)) &&
            !offers.Select(o => o.ChessId).SequenceEqual(oldOffers.Select(o => o.ChessId));
        bool paidRefresh = oldShop.RefreshCost > 0 && goldChange == -oldShop.RefreshCost;
        bool freeRefresh = oldShop.FreeRefreshes > 0 && shop.FreeRefreshes == oldShop.FreeRefreshes - 1 && goldChange == 0;
        return shop.Level == oldShop.Level && replacedOffers && (paidRefresh || freeRefresh)
            ? BobVoiceCue.ShopRefreshed : null;
    }

    private void UpdateTripleOpportunities(GarrisonSnapshot snapshot, GarrisonCharacter[] owned, GarrisonOffer[] offers)
    {
        var context = (snapshot.ActivityId, snapshot.PlayerIndex, snapshot.Round);
        if (_tripleContext != context)
        {
            _tripleContext = context;
            _announcedTriples.Clear();
        }

        // The local character inventory already includes the board and bench; positions aren't extra copies.
        var ids = owned.DistinctBy(c => c.InstanceId).Select(c => c.ChessId)
            .Concat(offers.DistinctBy(o => o.Slot).Select(o => o.ChessId));
        _newTriples.UnionWith(ids.Where(id => id.StartsWith("chess_char_", StringComparison.Ordinal)
                && id.EndsWith("_a", StringComparison.Ordinal))
            .GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() >= 3).Select(group => group.Key));
        _announcedTriples.IntersectWith(_newTriples);
        _newTriples.ExceptWith(_announcedTriples);
    }

    private static bool SameMemoryState(GarrisonSnapshot left, GarrisonSnapshot right) =>
        left.Round == right.Round && left.PlayerIndex == right.PlayerIndex && left.ActivityId == right.ActivityId &&
        left.Shop?.Level == right.Shop?.Level && left.Shop?.Coins == right.Shop?.Coins &&
        left.Shop?.FreeRefreshes == right.Shop?.FreeRefreshes &&
        (left.Shop?.Offers ?? []).OrderBy(o => o.Slot).SequenceEqual((right.Shop?.Offers ?? []).OrderBy(o => o.Slot)) &&
        SameInventory(left, right);

    private static bool SameInventory(GarrisonSnapshot left, GarrisonSnapshot right) =>
        (left.Characters ?? []).OrderBy(c => c.InstanceId).Select(c => (c.InstanceId, c.ChessId))
            .SequenceEqual((right.Characters ?? []).OrderBy(c => c.InstanceId).Select(c => (c.InstanceId, c.ChessId))) &&
        (left.Equipment ?? []).OrderBy(c => c.InstanceId).SequenceEqual((right.Equipment ?? []).OrderBy(c => c.InstanceId)) &&
        (left.Magic ?? []).OrderBy(c => c.InstanceId).SequenceEqual((right.Magic ?? []).OrderBy(c => c.InstanceId));
}
