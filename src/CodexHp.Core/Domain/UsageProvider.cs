namespace CodexHp.Core.Domain;

public enum UsageProvider
{
    Codex,
    Claude,
}

// Describes which provider the overlay shows. Null in the reducer keeps the Codex-only presentation.
public sealed record ProviderPresentation(
    UsageProvider Provider,
    bool IsLabelEmphasized = false,
    OverlayTooltipRow? OtherProviderSummary = null);

public sealed record ProviderSelection(UsageProvider Provider, bool IsLabelEmphasized);
