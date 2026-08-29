using CodexHp.Core.Settings;

namespace CodexHp.Core.Domain;

public static class TokenGraphViewport
{
    public const int BucketSeconds = 15;

    private const int ChartLeftInset = 4;
    private const int ChartRightInset = 6;

    public static int ChartLeft(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return appearance.GaugePaneWidth + ChartLeftInset;
    }

    public static int ChartRight(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return appearance.OverlayWidth - ChartRightInset;
    }

    public static int CalculateVisibleBucketCount(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        var barWidth = Math.Max(1, appearance.GraphBarWidth);
        var gap = Math.Max(0, appearance.GraphBarGap);
        var slotWidth = barWidth + gap;
        var chartLeft = ChartLeft(appearance);
        var firstBarLeft = ChartRight(appearance) - barWidth;
        if (firstBarLeft < chartLeft)
        {
            return 0;
        }

        return ((firstBarLeft - chartLeft) / slotWidth) + 1;
    }

    public static TimeSpan CalculateVisibleDuration(AppearanceSettings appearance) =>
        TimeSpan.FromSeconds((long)CalculateVisibleBucketCount(appearance) * BucketSeconds);
}
