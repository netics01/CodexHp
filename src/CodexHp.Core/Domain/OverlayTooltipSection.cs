namespace CodexHp.Core.Domain;

// Presentation data is produced with the plain-text tooltip, never parsed from English sentences.
public sealed record OverlayTooltipRow(string Label, string Value)
{
    // Optional mini gauges drawn in place of Value, in the overlay's gauge colors. Value stays the plain-text form.
    public IReadOnlyList<TooltipGauge>? Gauges { get; init; }

    // Short status drawn under the gauges, such as a service issue.
    public string? GaugeNote { get; init; }

    public bool Equals(OverlayTooltipRow? other) =>
        other is not null
        && this.Label == other.Label
        && this.Value == other.Value
        && this.GaugeNote == other.GaugeNote
        && (this.Gauges ?? []).SequenceEqual(other.Gauges ?? []);

    public override int GetHashCode() => HashCode.Combine(this.Label, this.Value, this.GaugeNote, this.Gauges?.Count ?? 0);
}

public enum TooltipGaugeKind
{
    FiveHour,
    Weekly,
}

public sealed record TooltipGauge(TooltipGaugeKind Kind, int RemainingPercent);

public sealed record OverlayTooltipSection(
    string? Title,
    IReadOnlyList<OverlayTooltipRow> Rows,
    bool IsWarning = false)
{
    public bool Equals(OverlayTooltipSection? other) =>
        other is not null
        && this.Title == other.Title
        && this.IsWarning == other.IsWarning
        && this.Rows.SequenceEqual(other.Rows);

    public override int GetHashCode() => HashCode.Combine(this.Title, this.IsWarning, this.Rows.Count);
}
