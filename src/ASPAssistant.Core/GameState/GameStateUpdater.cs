using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.GameState;

public static class GameStateUpdater
{
    public static void UpdateShopTracking(List<ShopItem> shopItems, List<TrackingEntry> tracked)
    {
        var trackedNames = new HashSet<string>(tracked.Select(t => t.Name));
        foreach (var item in shopItems)
        {
            item.IsTracked = trackedNames.Contains(item.Name);
        }
    }

    public static Dictionary<string, int> ComputeCovenantCounts(
        List<(string Name, int Count)> ownedOperators,
        List<Operator> operatorDatabase)
    {
        var counts = new Dictionary<string, int>();
        var dbLookup = operatorDatabase
            .GroupBy(o => o.Name)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (name, count) in ownedOperators)
        {
            if (!dbLookup.TryGetValue(name, out var op))
                continue;

            if (!string.IsNullOrEmpty(op.CoreCovenant))
            {
                counts.TryGetValue(op.CoreCovenant, out var existing);
                counts[op.CoreCovenant] = existing + count;
            }

            foreach (var covenant in op.AdditionalCovenants)
            {
                counts.TryGetValue(covenant, out var existing);
                counts[covenant] = existing + count;
            }
        }

        return counts;
    }
}
