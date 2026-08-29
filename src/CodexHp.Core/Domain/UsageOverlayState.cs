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
    string? StatusStripeTooltip);
