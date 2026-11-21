using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

internal static class DefaultAppearanceFactory
{
    private const int MinimumResetOverlayWidth = 120;
    private const int MaximumOverlayWidth = 4096;
    private const int TwentyMinuteBucketCount = 20 * 60 / TokenGraphViewport.BucketSeconds;

    public static AppearanceSettings? CreateInitial(
        AppSettings template,
        IReadOnlyList<DisplayEnvironment> displays)
    {
        if (displays.Count == 0 || OverlayDisplayResolver.ChooseInitialHeight(template, displays) is not { } height)
        {
            return null;
        }

        var defaults = AppearanceSettings.Default;
        var heightRatio = (double)height / defaults.OverlayHeight;
        int ScaleWidth(int width) => (int)Math.Round(width * heightRatio, MidpointRounding.AwayFromZero);
        var initial = template.Appearance with
        {
            OverlayHeight = height,
            OverlayWidth = ScaleWidth(defaults.OverlayWidth),
            GaugePaneWidth = ScaleWidth(defaults.GaugePaneWidth),
        };

        var minimumWidth = Math.Max(SettingsValidator.MinimumOverlayWidth,
            initial.GaugePaneWidth + SettingsValidator.MinimumChartWidth);
        while (initial.OverlayWidth > minimumWidth
            && TokenGraphViewport.CalculateVisibleBucketCount(
                OverlayDisplayResolver.Resolve(template with { Appearance = initial }, displays).Appearance)
                > TwentyMinuteBucketCount)
        {
            initial = initial with { OverlayWidth = initial.OverlayWidth - 1 };
        }

        return initial;
    }

    public static AppearanceSettings CreateForReset(
        AppSettings template,
        Func<AppSettings, EffectiveAppearanceSettings> resolveAppearance)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resolveAppearance);

        var preferredWidth = Math.Clamp(
            template.Appearance.OverlayWidth,
            MinimumResetOverlayWidth,
            MaximumOverlayWidth);
        var bestAppearance = template.Appearance with { OverlayWidth = preferredWidth };
        var bestDifference = int.MaxValue;
        var bestWidthDistance = int.MaxValue;
        var minimumWidth = MinimumResetOverlayWidth;
        var maximumWidth = MaximumOverlayWidth;

        while (minimumWidth <= maximumWidth)
        {
            var width = minimumWidth + ((maximumWidth - minimumWidth) / 2);
            var candidate = template.Appearance with { OverlayWidth = width };
            var effective = resolveAppearance(template with { Appearance = candidate });
            var bucketCount = TokenGraphViewport.CalculateVisibleBucketCount(effective);
            var difference = Math.Abs(bucketCount - TwentyMinuteBucketCount);
            var widthDistance = Math.Abs(width - preferredWidth);
            if (difference < bestDifference
                || (difference == bestDifference && widthDistance < bestWidthDistance))
            {
                bestAppearance = candidate;
                bestDifference = difference;
                bestWidthDistance = widthDistance;
            }

            if (bucketCount < TwentyMinuteBucketCount)
            {
                minimumWidth = width + 1;
            }
            else if (bucketCount > TwentyMinuteBucketCount)
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
