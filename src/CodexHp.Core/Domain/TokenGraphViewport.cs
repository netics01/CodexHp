using CodexHp.Core.Settings;

namespace CodexHp.Core.Domain;

public static class TokenGraphViewport
{
    public const int BucketSeconds = 15;

    public const int ChartLeftInsetDip = 4;
    public const int ChartRightInsetDip = 6;

    public static int ChartLeft(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return appearance.GaugePaneWidth + ChartLeftInsetDip;
    }

    public static int ChartRight(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return appearance.OverlayWidth - ChartRightInsetDip;
    }

    public static int ChartLeft(EffectiveAppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return appearance.GaugePaneWidth + ChartLeftInsetDip;
    }

    public static int ChartRight(EffectiveAppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return appearance.OverlayWidth - ChartRightInsetDip;
    }

    public static int CalculateVisibleBucketCount(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return CalculateVisibleBucketCount(
            appearance.GraphBarWidth,
            appearance.GraphBarGap,
            ChartLeft(appearance),
            ChartRight(appearance));
    }

    public static TimeSpan CalculateVisibleDuration(AppearanceSettings appearance) =>
        TimeSpan.FromSeconds((long)CalculateVisibleBucketCount(appearance) * BucketSeconds);

    public static int CalculateVisibleBucketCount(EffectiveAppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return CalculateVisibleBucketCount(
            appearance.GraphBarWidth,
            appearance.GraphBarGap,
            ChartLeft(appearance),
            ChartRight(appearance));
    }

    public static TimeSpan CalculateVisibleDuration(EffectiveAppearanceSettings appearance) =>
        TimeSpan.FromSeconds((long)CalculateVisibleBucketCount(appearance) * BucketSeconds);

    private static int CalculateVisibleBucketCount(
        int configuredBarWidth,
        int configuredGap,
        int chartLeft,
        int chartRight)
    {
        var barWidth = Math.Max(1, configuredBarWidth);
        var gap = Math.Max(0, configuredGap);
        var slotWidth = barWidth + gap;
        var firstBarLeft = chartRight - barWidth;
        if (firstBarLeft < chartLeft)
        {
            return 0;
        }

        return ((firstBarLeft - chartLeft) / slotWidth) + 1;
    }
}
