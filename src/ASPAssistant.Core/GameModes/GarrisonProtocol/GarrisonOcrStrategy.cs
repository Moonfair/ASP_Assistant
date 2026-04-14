namespace ASPAssistant.Core.GameModes.GarrisonProtocol;

public class GarrisonOcrStrategy : IOcrStrategy
{
    private static readonly List<OcrRegionDefinition> Regions =
    [
        new("Shop", XPercent: 0.00, YPercent: 0.50, WidthPercent: 1.00, HeightPercent: 0.50),
        new("Gold", XPercent: 0.85, YPercent: 0.02, WidthPercent: 0.12, HeightPercent: 0.05),
        new("Round", XPercent: 0.45, YPercent: 0.02, WidthPercent: 0.10, HeightPercent: 0.05),
        new("Field", XPercent: 0.10, YPercent: 0.72, WidthPercent: 0.80, HeightPercent: 0.15),
        new("Bench", XPercent: 0.10, YPercent: 0.88, WidthPercent: 0.80, HeightPercent: 0.10),
        // Ban screen: scan the upper portion of the screen for the unique header text.
        new("BanFactionPanel", XPercent: 0.05, YPercent: 0.00, WidthPercent: 0.25, HeightPercent: 0.90),
    ];

    public IReadOnlyList<OcrRegionDefinition> GetScanRegions() => Regions;
}
