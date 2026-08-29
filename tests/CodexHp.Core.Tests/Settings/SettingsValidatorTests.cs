using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Settings;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void Validate_preserves_valid_values()
    {
        var input = AppSettings.Default with
        {
            Appearance = AppSettings.Default.Appearance with
            {
                OverlayWidth = 400,
                OverlayHeight = 60,
                GaugePaneWidth = 120,
                GraphBarWidth = 5,
                GraphBarGap = 2,
                StatusStripeWidth = 6,
            },
            Location = new OverlayLocationSettings("DISPLAY2", 24, 32),
        };

        var result = SettingsValidator.Validate(input);

        Assert.Equal(input, result.Settings);
        Assert.Empty(result.CorrectedFields);
    }

    [Fact]
    public void Validate_restores_only_invalid_appearance_values()
    {
        var input = AppSettings.Default with
        {
            Appearance = AppSettings.Default.Appearance with
            {
                OverlayWidth = 400,
                OverlayHeight = -1,
                GaugePaneWidth = 120,
                GraphBarWidth = 0,
                GraphBarGap = 2,
                StatusStripeWidth = 99,
            },
        };

        var result = SettingsValidator.Validate(input);

        Assert.Equal(400, result.Settings.Appearance.OverlayWidth);
        Assert.Equal(AppSettings.Default.Appearance.OverlayHeight, result.Settings.Appearance.OverlayHeight);
        Assert.Equal(120, result.Settings.Appearance.GaugePaneWidth);
        Assert.Equal(AppSettings.Default.Appearance.GraphBarWidth, result.Settings.Appearance.GraphBarWidth);
        Assert.Equal(2, result.Settings.Appearance.GraphBarGap);
        Assert.Equal(AppSettings.Default.Appearance.StatusStripeWidth, result.Settings.Appearance.StatusStripeWidth);
        Assert.Equal(
            ["Appearance.OverlayHeight", "Appearance.GraphBarWidth", "Appearance.StatusStripeWidth"],
            result.CorrectedFields);
    }

    [Fact]
    public void Validate_restores_gauge_width_when_it_leaves_no_chart_space()
    {
        var input = AppSettings.Default with
        {
            Appearance = AppSettings.Default.Appearance with
            {
                OverlayWidth = 160,
                GaugePaneWidth = 150,
            },
        };

        var result = SettingsValidator.Validate(input);

        Assert.Equal(AppSettings.Default.Appearance.GaugePaneWidth, result.Settings.Appearance.GaugePaneWidth);
        Assert.Equal(["Appearance.GaugePaneWidth"], result.CorrectedFields);
    }
}
