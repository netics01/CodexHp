using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Settings;

public sealed class SettingsDefaultsTests
{
    [Fact]
    public void Defaults_match_the_approved_product_requirements()
    {
        var settings = AppSettings.Default;

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.True(settings.StartWithWindows);
        Assert.False(settings.ShowOnlyWhenChatGptRunning);
        Assert.Equal("#3A8EFF", settings.Colors.ManaBar.ToHex());
        Assert.Equal("#DC4856", settings.Colors.HpBar.ToHex());
        Assert.Equal("#FFFFFF", settings.Colors.RefreshGauge.ToHex());
        Assert.Equal("#F5A623", settings.Colors.ServiceIssue.ToHex());
        Assert.Equal("#808080", settings.Colors.ServiceUnknown.ToHex());
        Assert.Equal("#2667CD", settings.Colors.TokenLow.ToHex());
        Assert.Equal("#DC4856", settings.Colors.TokenHigh.ToHex());
        Assert.Equal(144, settings.Appearance.OverlayWidth);
        Assert.Equal(34, settings.Appearance.OverlayHeight);
        Assert.Equal(50, settings.Appearance.GaugePaneWidth);
        Assert.Equal(1, settings.Appearance.GraphBarWidth);
        Assert.Equal(0, settings.Appearance.GraphBarGap);
        Assert.Equal(2, settings.Appearance.StatusStripeWidth);
        Assert.Null(settings.Location.MonitorId);
        Assert.Null(settings.Location.MonitorKey);
        Assert.Equal(OverlayPlacementTarget.Taskbar, settings.Location.Target);
        Assert.Null(settings.Location.NormalizedX);
        Assert.Null(settings.Location.NormalizedY);
    }

    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#3A8EFF", 58, 142, 255)]
    [InlineData("#ffffff", 255, 255, 255)]
    public void ColorValue_parses_rgb_hex(string value, byte red, byte green, byte blue)
    {
        var color = ColorValue.Parse(value);

        Assert.Equal(red, color.Red);
        Assert.Equal(green, color.Green);
        Assert.Equal(blue, color.Blue);
        Assert.Equal(value.ToUpperInvariant(), color.ToHex());
    }

    [Theory]
    [InlineData("")]
    [InlineData("3A8EFF")]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    public void ColorValue_rejects_invalid_values(string value)
    {
        Assert.False(ColorValue.TryParse(value, out _));
        Assert.Throws<FormatException>(() => ColorValue.Parse(value));
    }
}
