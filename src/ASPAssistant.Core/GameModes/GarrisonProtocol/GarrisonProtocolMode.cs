namespace ASPAssistant.Core.GameModes.GarrisonProtocol;

public class GarrisonProtocolMode : IGameMode
{
    public string Name => "卫戍协议";
    public IOcrStrategy OcrStrategy { get; } = new GarrisonOcrStrategy();
}
