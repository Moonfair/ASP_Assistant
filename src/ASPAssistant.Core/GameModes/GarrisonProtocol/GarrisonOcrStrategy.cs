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
        // Enemy section: scan the upper-left area for "本局遭遇敌方" to anchor the enemy block.
        new("EnemySectionScan", XPercent: 0.00, YPercent: 0.00, WidthPercent: 0.45, HeightPercent: 0.55),
        // Ban screen detection: scan the header area for "确认本局信息".
        new("BanConfirmText", XPercent: 0.25, YPercent: 0.00, WidthPercent: 0.50, HeightPercent: 0.10),
        // Ban screen: scan the left faction panel for covenant OCR pre-filter.
        new("BanFactionPanel", XPercent: 0.05, YPercent: 0.00, WidthPercent: 0.25, HeightPercent: 0.90),
        // Ban screen: left-side panel scanned to locate "核心盟约" / "附加盟约" section headers.
        // The found text bounding-box top-left is used as the anchor for the covenant block.
        new("BanSectionScan", XPercent: 0.00, YPercent: 0.30, WidthPercent: 0.45, HeightPercent: 0.70),
    ];

    public IReadOnlyList<OcrRegionDefinition> GetScanRegions() => Regions;
}
