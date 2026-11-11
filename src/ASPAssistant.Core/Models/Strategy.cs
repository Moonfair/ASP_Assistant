namespace ASPAssistant.Core.Models;

public sealed record Strategy
{
    public string Name { get; init; } = "";
    public string Sponsor { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconPath { get; init; } = "";
}
