namespace CodexHp.Core.Domain;

// Presentation data is produced with the plain-text tooltip, never parsed from English sentences.
public sealed record OverlayTooltipRow(string Label, string Value);

public sealed record OverlayTooltipSection(
    string? Title,
    IReadOnlyList<OverlayTooltipRow> Rows,
    bool IsWarning = false);
