namespace CodexHp.Core.Settings;

public sealed record AppearanceSettings(
    int OverlayWidth,
    int OverlayHeight,
    int GaugePaneWidth,
    int GraphBarWidth,
    int GraphBarGap,
    int StatusStripeWidth)
{
    public static AppearanceSettings Default { get; } = new(
        OverlayWidth: 144,
        OverlayHeight: 34,
        GaugePaneWidth: 50,
        GraphBarWidth: 1,
        GraphBarGap: 0,
        StatusStripeWidth: 2);
}

public sealed record EffectiveAppearanceSettings(
    int OverlayWidth,
    int OverlayHeight,
    int GaugePaneWidth,
    int GraphBarWidth,
    int GraphBarGap,
    int StatusStripeWidth);
