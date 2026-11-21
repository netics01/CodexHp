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
        OverlayWidth: 130,
        OverlayHeight: 32,
        GaugePaneWidth: 48,
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
    int StatusStripeWidth,
    double DisplayScaleX = 1,
    double DisplayScaleY = 1);
