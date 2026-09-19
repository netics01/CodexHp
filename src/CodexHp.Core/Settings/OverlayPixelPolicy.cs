namespace CodexHp.Core.Settings;

// Layout dimensions are DIP, calibrated to the original 200% appearance.
// Only graph hairlines/dashes below remain fixed physical pixels.
// Round each dimension once; preserve zero spacing and at least one pixel for positive dimensions.
// Saved appearance values remain DIP and must never be rewritten on DPI changes.
// TokenGraphViewport shares this policy with rendering, polling, and Settings.
public static class OverlayPixelPolicy
{
    public const double LeftInsetDip = 2;
    public const double VerticalInsetDip = 0;
    public const double StatusVerticalInsetDip = 0;
    public const double GaugeRightInsetDip = 0;
    public const double RefreshHeightDip = 1;
    public const double GaugeGroupGapDip = 1;
    public const double RefreshSeparatorDip = 1;
    public const double StatusGapDip = 1;
    public const double ChartLeftInsetDip = 2;
    public const double ChartRightInsetDip = 0;
    public const double ChartTopInsetDip = 0;
    // Empty space below the baseline; the baseline itself stays inside the bitmap.
    public const double ChartBottomInsetDip = 0;
    public const double PositionOutlineDip = 2;
    public const double MinimumGaugeFontDip = 8.5;
    public const double MinimumMessageFontDip = 5;
    public const double MaximumMessageFontDip = 8;

    public const int GraphBaselinePixels = 1;
    public const int GraphGridWidthPixels = 1;
    public const int GraphGridDotPixels = 2;
    public const int GraphGridPeriodPixels = 4;

    public static int ToPixels(double dip, double scale) => dip == 0 ? 0 : Math.Max(
        1,
        (int)Math.Round(
            dip * (double.IsFinite(scale) && scale > 0 ? scale : 1),
            MidpointRounding.AwayFromZero));
}
