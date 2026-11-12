namespace ASPAssistant.Core.Models;

public enum OperatorFilterKind
{
    Separator,
    Availability,
    SelfSelected,
    Tier,
    CoreCovenant,
    OtherCovenant,
    Trait,
    Stacking,
    TriggerTiming
}

/// <summary>A summary label with its original filter identity, independent of display text.</summary>
public sealed record OperatorFilterTag(OperatorFilterKind Kind, object? Value, string Label)
{
    public bool IsSeparator => Kind == OperatorFilterKind.Separator;
}
