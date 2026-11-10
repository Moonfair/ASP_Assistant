namespace ASPAssistant.Core.Models;

/// <summary>A complete recognition result; null means no confirmed result for this round.</summary>
public sealed record CovenantAvailability(
    IReadOnlySet<string> EnabledCore,
    IReadOnlySet<string> EnabledAdditional);
