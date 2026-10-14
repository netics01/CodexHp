namespace CodexHp.Core.Domain;

public enum UsageProvider
{
    Codex,
    Claude,
}

// Describes which provider the overlay shows. Null in the reducer keeps the Codex-only presentation.
public sealed record ProviderPresentation(
    UsageProvider Provider,
    double LabelEmphasis = 0,
    OverlayTooltipRow? OtherProviderSummary = null);

// LabelEmphasis runs from 1 (just switched) to 0 (settled).
public sealed record ProviderSelection(UsageProvider Provider, double LabelEmphasis);
