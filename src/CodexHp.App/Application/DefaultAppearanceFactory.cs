using CodexHp.Core.Domain;
using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

internal static class DefaultAppearanceFactory
{
    private const int MinimumOverlayWidth = 120;
    private const int MaximumOverlayWidth = 4096;
    private const int TargetVisibleBucketCount = 20 * 60 / TokenGraphViewport.BucketSeconds;

    public static AppearanceSettings Create(
        AppSettings template,
        Func<AppSettings, EffectiveAppearanceSettings> resolveAppearance)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resolveAppearance);

        var preferredWidth = Math.Clamp(
            template.Appearance.OverlayWidth,
            MinimumOverlayWidth,
            MaximumOverlayWidth);
        var bestAppearance = template.Appearance with { OverlayWidth = preferredWidth };
        var bestDifference = int.MaxValue;
        var bestWidthDistance = int.MaxValue;
        var minimumWidth = MinimumOverlayWidth;
        var maximumWidth = MaximumOverlayWidth;

        while (minimumWidth <= maximumWidth)
        {
            var width = minimumWidth + ((maximumWidth - minimumWidth) / 2);
            var candidate = template.Appearance with { OverlayWidth = width };
            var effective = resolveAppearance(template with { Appearance = candidate });
            var bucketCount = TokenGraphViewport.CalculateVisibleBucketCount(effective);
            var difference = Math.Abs(bucketCount - TargetVisibleBucketCount);
            var widthDistance = Math.Abs(width - preferredWidth);
            if (difference < bestDifference
                || (difference == bestDifference && widthDistance < bestWidthDistance))
            {
                bestAppearance = candidate;
                bestDifference = difference;
                bestWidthDistance = widthDistance;
            }

            if (bucketCount < TargetVisibleBucketCount)
            {
                minimumWidth = width + 1;
            }
            else if (bucketCount > TargetVisibleBucketCount)
            {
                maximumWidth = width - 1;
            }
            else
            {
                return candidate;
            }
        }

        return bestAppearance;
    }
}
