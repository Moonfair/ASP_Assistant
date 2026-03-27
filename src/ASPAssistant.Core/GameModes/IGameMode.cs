namespace ASPAssistant.Core.GameModes;

public record OcrRegionDefinition(
    string Name,
    double XPercent,
    double YPercent,
    double WidthPercent,
    double HeightPercent);

public interface IOcrStrategy
{
    IReadOnlyList<OcrRegionDefinition> GetScanRegions();
}

public interface IGameMode
{
    string Name { get; }
    IOcrStrategy OcrStrategy { get; }
}
