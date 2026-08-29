namespace CodexHp.Core.Settings;

public sealed record SettingsValidationResult(AppSettings Settings, IReadOnlyList<string> CorrectedFields);

public static class SettingsValidator
{
    private const int MinimumOverlayWidth = 120;
    private const int MaximumOverlayWidth = 4096;
    private const int MinimumOverlayHeight = 24;
    private const int MaximumOverlayHeight = 512;
    private const int MinimumGaugePaneWidth = 20;
    private const int MinimumChartWidth = 20;
    private const int MinimumGraphBarWidth = 1;
    private const int MaximumGraphBarWidth = 20;
    private const int MinimumGraphBarGap = 0;
    private const int MaximumGraphBarGap = 20;
    private const int MinimumStatusStripeWidth = 1;
    private const int MaximumStatusStripeWidth = 12;

    public static SettingsValidationResult Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var correctedFields = new List<string>();
        var appearance = settings.Appearance;

        var overlayWidth = ValidateRange(
            appearance.OverlayWidth,
            MinimumOverlayWidth,
            MaximumOverlayWidth,
            AppearanceSettings.Default.OverlayWidth,
            "Appearance.OverlayWidth",
            correctedFields);
        var overlayHeight = ValidateRange(
            appearance.OverlayHeight,
            MinimumOverlayHeight,
            MaximumOverlayHeight,
            AppearanceSettings.Default.OverlayHeight,
            "Appearance.OverlayHeight",
            correctedFields);
        var maximumGaugePaneWidth = Math.Max(MinimumGaugePaneWidth, overlayWidth - MinimumChartWidth);
        var gaugePaneWidth = ValidateRange(
            appearance.GaugePaneWidth,
            MinimumGaugePaneWidth,
            maximumGaugePaneWidth,
            AppearanceSettings.Default.GaugePaneWidth,
            "Appearance.GaugePaneWidth",
            correctedFields);
        var graphBarWidth = ValidateRange(
            appearance.GraphBarWidth,
            MinimumGraphBarWidth,
            MaximumGraphBarWidth,
            AppearanceSettings.Default.GraphBarWidth,
            "Appearance.GraphBarWidth",
            correctedFields);
        var graphBarGap = ValidateRange(
            appearance.GraphBarGap,
            MinimumGraphBarGap,
            MaximumGraphBarGap,
            AppearanceSettings.Default.GraphBarGap,
            "Appearance.GraphBarGap",
            correctedFields);
        var statusStripeWidth = ValidateRange(
            appearance.StatusStripeWidth,
            MinimumStatusStripeWidth,
            MaximumStatusStripeWidth,
            AppearanceSettings.Default.StatusStripeWidth,
            "Appearance.StatusStripeWidth",
            correctedFields);

        var validated = settings with
        {
            Appearance = new AppearanceSettings(
                overlayWidth,
                overlayHeight,
                gaugePaneWidth,
                graphBarWidth,
                graphBarGap,
                statusStripeWidth),
        };

        return new SettingsValidationResult(validated, correctedFields);
    }

    private static int ValidateRange(
        int value,
        int minimum,
        int maximum,
        int defaultValue,
        string field,
        ICollection<string> correctedFields)
    {
        if (value >= minimum && value <= maximum)
        {
            return value;
        }

        correctedFields.Add(field);
        return defaultValue;
    }
}
