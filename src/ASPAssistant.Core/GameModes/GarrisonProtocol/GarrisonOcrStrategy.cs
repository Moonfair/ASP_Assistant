namespace ASPAssistant.Core.GameModes.GarrisonProtocol;

public class GarrisonOcrStrategy : IOcrStrategy
{
    private static readonly List<OcrRegionDefinition> Regions =
    [
        new("Shop", XPercent: 0.00, YPercent: 0.67, WidthPercent: 1.00, HeightPercent: 0.33),
        new("Gold", XPercent: 0.85, YPercent: 0.02, WidthPercent: 0.12, HeightPercent: 0.05),
        new("Round", XPercent: 0.45, YPercent: 0.02, WidthPercent: 0.10, HeightPercent: 0.05),
        new("Field", XPercent: 0.10, YPercent: 0.72, WidthPercent: 0.80, HeightPercent: 0.15),
        new("Bench", XPercent: 0.10, YPercent: 0.88, WidthPercent: 0.80, HeightPercent: 0.10),
        // Ban confirmation screen: left faction/covenant panel (faction name labels)
        new("BanFactionPanel", XPercent: 0.050, YPercent: 0.218, WidthPercent: 0.180, HeightPercent: 0.726),
    ];

    public IReadOnlyList<OcrRegionDefinition> GetScanRegions() => Regions;
}
