using CodexHp.Core.Settings;

namespace CodexHp.Core.Domain;

public sealed record GaugeDisplayState(
    int? RemainingPercent,
    double RefreshFraction,
    bool IsStale);

public sealed record UsageOverlayState(
    bool IsVisible,
    GaugeDisplayState ManaBar,
    GaugeDisplayState HpBar,
    IReadOnlyList<int> TokenBuckets,
    ColorValue? StatusStripeColor,
    string? StatusStripeTooltip,
    string? ContentMessage = null,
    string? ContentTooltip = null)
{
    public ServiceHealthState? ServiceHealth { get; init; }

    public ResetCreditsDisplayState? BankedResets { get; init; }

    public IReadOnlyList<OverlayTooltipSection> TooltipSections { get; init; } = [];

    // Faint graph caption naming the shown provider. Null in the Codex-only presentation.
    public string? ProviderLabel { get; init; }

    // 1 right after a provider switch, easing to 0 as the label returns to its faint resting brightness.
    public double ProviderLabelEmphasis { get; init; }

    public string? Tooltip
    {
        get
        {
            var sections = new[] { ContentTooltip, StatusStripeTooltip }
                .Where(section => !string.IsNullOrWhiteSpace(section))
                .Select(section => section!.Trim())
                .ToArray();
            return sections.Length == 0 ? null : string.Join("\r\n\r\n", sections);
        }
    }
}
