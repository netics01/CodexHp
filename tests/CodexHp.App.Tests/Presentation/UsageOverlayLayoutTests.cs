using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class UsageOverlayLayoutTests
{
    [Fact]
    public void Default_layout_keeps_gauges_refresh_tracks_and_chart_in_separate_bounds()
    {
        var layout = UsageOverlayRenderer.CreateLayout(SampleState(), ReferencePhysicalSettings, false);

        Assert.Equal(288, layout.Width);
        Assert.Equal(68, layout.Height);
        Assert.Equal(new LayoutRect(4, 2, 93, 27), Single(layout, OverlayElementRole.ManaTrack).Bounds);
        Assert.Equal(new LayoutRect(4, 2, 69, 27), Single(layout, OverlayElementRole.ManaFill).Bounds);
        Assert.Equal(new LayoutRect(4, 31, 93, 2), Single(layout, OverlayElementRole.ManaRefreshTrack).Bounds);
        Assert.Equal(new LayoutRect(4, 31, 46, 2), Single(layout, OverlayElementRole.ManaRefreshFill).Bounds);
        Assert.Equal(new LayoutRect(4, 35, 93, 27), Single(layout, OverlayElementRole.HpTrack).Bounds);
        Assert.Equal(new LayoutRect(4, 35, 37, 27), Single(layout, OverlayElementRole.HpFill).Bounds);
        Assert.Equal(new LayoutRect(4, 64, 93, 2), Single(layout, OverlayElementRole.HpRefreshTrack).Bounds);
        Assert.Equal(new LayoutRect(4, 64, 23, 2), Single(layout, OverlayElementRole.HpRefreshFill).Bounds);
        Assert.Equal(16, Single(layout, OverlayElementRole.ManaText).FontSize);
        Assert.Equal(new LayoutRect(104, 62, 178, 1), Single(layout, OverlayElementRole.GraphBaseline).Bounds);
        Assert.All(
            layout.Commands.Where(command => command.Role is OverlayElementRole.TokenBar or OverlayElementRole.GraphGridDot),
            command =>
            {
                Assert.True(command.Bounds.Left >= 104);
                Assert.True(command.Bounds.Right <= 282);
                Assert.True(command.Bounds.Top >= 4);
                Assert.True(command.Bounds.Bottom <= 62);
            });
    }

    [Fact]
    public void Configured_shape_values_change_each_affected_region()
    {
        var settings = AppSettings.Default with
        {
            Appearance = new AppearanceSettings(
                OverlayWidth: 400,
                OverlayHeight: 60,
                GaugePaneWidth: 100,
                GraphBarWidth: 5,
                GraphBarGap: 2,
                StatusStripeWidth: 6),
        };
        var state = SampleState() with { StatusStripeColor = ColorValue.Parse("#F5A623") };

        var layout = UsageOverlayRenderer.CreateLayout(state, settings, false);

        Assert.Equal(400, layout.Width);
        Assert.Equal(60, layout.Height);
        Assert.Equal(new LayoutRect(4, 2, 6, 56), Single(layout, OverlayElementRole.StatusStripe).Bounds);
        Assert.Equal(12, Single(layout, OverlayElementRole.ManaTrack).Bounds.Left);
        Assert.Equal(97, Single(layout, OverlayElementRole.ManaTrack).Bounds.Right);
        Assert.Equal(5, layout.Commands.First(command => command.Role == OverlayElementRole.TokenBar).Bounds.Width);
        Assert.Equal(104, Single(layout, OverlayElementRole.GraphBaseline).Bounds.Left);
        Assert.Equal(394, Single(layout, OverlayElementRole.GraphBaseline).Bounds.Right);
    }

    [Fact]
    public void Graph_baseline_uses_the_shared_token_viewport_bounds()
    {
        var settings = ReferencePhysicalSettings;

        var baseline = Single(
            UsageOverlayRenderer.CreateLayout(SampleState(), settings, false),
            OverlayElementRole.GraphBaseline);

        Assert.Equal(TokenGraphViewport.ChartLeft(settings.Appearance), baseline.Bounds.Left);
        Assert.Equal(TokenGraphViewport.ChartRight(settings.Appearance), baseline.Bounds.Right);
    }

    [Fact]
    public void Effective_physical_appearance_controls_both_bitmap_and_internal_layout()
    {
        var presentation = new OverlayPresentationSettings(
            AppSettings.Default.Colors,
            new EffectiveAppearanceSettings(288, 68, 100, 2, 0, 4));

        var layout = UsageOverlayRenderer.CreateLayout(SampleState(), presentation, false);

        Assert.Equal(288, layout.Width);
        Assert.Equal(68, layout.Height);
        Assert.Equal(100, Single(layout, OverlayElementRole.GraphBaseline).Bounds.Left - 4);
        Assert.Equal(2, layout.Commands.First(command => command.Role == OverlayElementRole.TokenBar).Bounds.Width);
    }

    private static OverlayDrawCommand Single(UsageOverlayLayout layout, OverlayElementRole role) =>
        Assert.Single(layout.Commands, command => command.Role == role);

    private static AppSettings ReferencePhysicalSettings => AppSettings.Default with
    {
        Appearance = new AppearanceSettings(288, 68, 100, 2, 0, 4),
    };

    private static UsageOverlayState SampleState() => new(
        IsVisible: true,
        ManaBar: new GaugeDisplayState(75, 0.5, false),
        HpBar: new GaugeDisplayState(40, 0.25, false),
        TokenBuckets: [10_000, 25_000, 100_000],
        StatusStripeColor: null,
        StatusStripeTooltip: null);
}
