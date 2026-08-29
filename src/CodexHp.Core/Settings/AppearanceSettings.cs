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
        OverlayWidth: 288,
        OverlayHeight: 68,
        GaugePaneWidth: 100,
        GraphBarWidth: 2,
        GraphBarGap: 0,
        StatusStripeWidth: 4);
}
