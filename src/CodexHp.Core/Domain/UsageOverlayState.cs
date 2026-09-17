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
