using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
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
        Assert.Equal(new LayoutRect(4, 0, 96, 31), Single(layout, OverlayElementRole.ManaTrack).Bounds);
        Assert.Equal(new LayoutRect(4, 0, 72, 31), Single(layout, OverlayElementRole.ManaFill).Bounds);
        Assert.Equal(new LayoutRect(4, 31, 96, 2), Single(layout, OverlayElementRole.ManaRefreshTrack).Bounds);
        Assert.Equal(new LayoutRect(4, 31, 48, 2), Single(layout, OverlayElementRole.ManaRefreshFill).Bounds);
        Assert.Equal(new LayoutRect(4, 35, 96, 31), Single(layout, OverlayElementRole.HpTrack).Bounds);
        Assert.Equal(new LayoutRect(4, 35, 38, 31), Single(layout, OverlayElementRole.HpFill).Bounds);
        Assert.Equal(new LayoutRect(4, 66, 96, 2), Single(layout, OverlayElementRole.HpRefreshTrack).Bounds);
        Assert.Equal(new LayoutRect(4, 66, 24, 2), Single(layout, OverlayElementRole.HpRefreshFill).Bounds);
        Assert.Equal(18, Single(layout, OverlayElementRole.ManaText).FontSize);
        Assert.Equal(
            Single(layout, OverlayElementRole.ManaTrack).Bounds.Bottom,
            Single(layout, OverlayElementRole.ManaRefreshTrack).Bounds.Top);
        Assert.Equal(
            Single(layout, OverlayElementRole.HpTrack).Bounds.Bottom,
            Single(layout, OverlayElementRole.HpRefreshTrack).Bounds.Top);
        Assert.Equal(
            2,
            Single(layout, OverlayElementRole.HpTrack).Bounds.Top
                - Single(layout, OverlayElementRole.ManaRefreshTrack).Bounds.Bottom);
        Assert.Equal(new LayoutRect(104, 67, 184, 1), Single(layout, OverlayElementRole.GraphBaseline).Bounds);
        Assert.All(
            layout.Commands.Where(command => command.Role is OverlayElementRole.TokenBar or OverlayElementRole.GraphGridDot),
            command =>
            {
                Assert.True(command.Bounds.Left >= 104);
                Assert.True(command.Bounds.Right <= 288);
                Assert.True(command.Bounds.Top >= 0);
                Assert.True(command.Bounds.Bottom <= 67);
            });
    }

    [Theory]
    [InlineData(34, 1.00, 15, 9)]
    [InlineData(43, 1.25, 20, 12)]
    [InlineData(51, 1.50, 22, 13)]
    [InlineData(68, 2.00, 31, 18)]
    [InlineData(85, 2.50, 38, 23)]
    public void Gauge_text_keeps_the_reference_proportion_without_dropping_below_eight_point_five_dip(
        int overlayHeight,
        double displayScale,
        int expectedRowHeight,
        int expectedFontHeight)
    {
        var presentation = new OverlayPresentationSettings(
            AppSettings.Default.Colors,
            new EffectiveAppearanceSettings(266, overlayHeight, 96, 2, 0, 4, displayScale, displayScale));

        var layout = UsageOverlayRenderer.CreateLayout(SampleState(), presentation, false);

        Assert.Equal(expectedRowHeight, Single(layout, OverlayElementRole.ManaTrack).Bounds.Height);
        Assert.Equal(expectedFontHeight, Single(layout, OverlayElementRole.ManaText).FontSize);
        Assert.Equal(expectedRowHeight, Single(layout, OverlayElementRole.HpTrack).Bounds.Height);
        Assert.Equal(expectedFontHeight, Single(layout, OverlayElementRole.HpText).FontSize);
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
        Assert.Equal(new LayoutRect(2, 0, 6, 60), Single(layout, OverlayElementRole.StatusStripe).Bounds);
        Assert.Equal(9, Single(layout, OverlayElementRole.ManaTrack).Bounds.Left);
        Assert.Equal(100, Single(layout, OverlayElementRole.ManaTrack).Bounds.Right);
        Assert.Equal(5, layout.Commands.First(command => command.Role == OverlayElementRole.TokenBar).Bounds.Width);
        Assert.Equal(102, Single(layout, OverlayElementRole.GraphBaseline).Bounds.Left);
        Assert.Equal(400, Single(layout, OverlayElementRole.GraphBaseline).Bounds.Right);
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
            new EffectiveAppearanceSettings(288, 68, 100, 2, 0, 4, 2, 2));

        var layout = UsageOverlayRenderer.CreateLayout(SampleState(), presentation, false);

        Assert.Equal(288, layout.Width);
        Assert.Equal(68, layout.Height);
        Assert.Equal(100, Single(layout, OverlayElementRole.GraphBaseline).Bounds.Left - 4);
        Assert.Equal(2, layout.Commands.First(command => command.Role == OverlayElementRole.TokenBar).Bounds.Width);
    }

    [Fact]
    public void Two_hundred_percent_dpi_uses_the_full_chart_area_without_time_alignment_padding()
    {
        var settings = AppSettings.Default with
        {
            Appearance = new AppearanceSettings(140, 34, 50, 1, 0, 2),
        };
        var monitor = new MonitorGeometry(
            "DISPLAY2",
            new PhysicalRect(0, 0, 3840, 2160),
            new PhysicalRect(0, 0, 3840, 2064),
            2,
            2,
            true,
            "MONITOR-STABLE-2");
        var resolution = OverlayDisplayResolver.Resolve(
            settings,
            [new DisplayEnvironment(monitor, new PhysicalRect(0, 2064, 3840, 96))]);
        var presentation = new OverlayPresentationSettings(
            settings.Colors,
            resolution.Appearance);

        var layout = UsageOverlayRenderer.CreateLayout(SampleState(), presentation, false);
        var baseline = Single(layout, OverlayElementRole.GraphBaseline).Bounds;
        var oldestGridLine = layout.Commands
            .Where(command => command.Role == OverlayElementRole.GraphGridDot)
            .Min(command => command.Bounds.Left);

        Assert.Equal(104, baseline.Left);
        Assert.Equal(280, baseline.Right);
        Assert.Equal(120, oldestGridLine);
        Assert.Equal(88, TokenGraphViewport.CalculateVisibleBucketCount(resolution.Appearance));
    }

    [Theory]
    [InlineData(1920, 1080, 1, 2, 1)]
    [InlineData(1920, 1080, 1.25, 3, 1)]
    [InlineData(2560, 1440, 1, 2, 1)]
    [InlineData(2560, 1440, 1.5, 3, 2)]
    [InlineData(3840, 2160, 1.5, 3, 2)]
    [InlineData(3840, 2160, 2, 4, 2)]
    [InlineData(3840, 2160, 2.5, 5, 3)]
    [InlineData(3840, 2160, 3, 6, 3)]
    public void Dpi_policy_scales_layout_but_keeps_graph_hairlines_in_physical_pixels(
        int screenWidth, int screenHeight, double scale, int expectedGap, int expectedRefreshHeight)
    {
        var settings = AppSettings.Default with { Appearance = new AppearanceSettings(133, 34, 48, 1, 0, 2) };
        var monitor = new MonitorGeometry("DISPLAY", new PhysicalRect(0, 0, screenWidth, screenHeight),
            new PhysicalRect(0, 0, screenWidth, screenHeight), scale, scale, true);
        var resolution = OverlayDisplayResolver.Resolve(settings, [new DisplayEnvironment(monitor, null)]);
        var appearance = resolution.Appearance;
        var layout = UsageOverlayRenderer.CreateLayout(SampleState(), new OverlayPresentationSettings(settings.Colors, appearance), false);
        var baseline = Single(layout, OverlayElementRole.GraphBaseline).Bounds;

        Assert.Equal(expectedGap, baseline.Left - Single(layout, OverlayElementRole.ManaTrack).Bounds.Right);
        Assert.Equal(0, Single(layout, OverlayElementRole.ManaTrack).Bounds.Top);
        Assert.InRange(layout.Height - Single(layout, OverlayElementRole.HpRefreshTrack).Bounds.Bottom, 0, 1);
        Assert.Equal(layout.Width, baseline.Right);
        Assert.Equal(0, layout.Commands.Where(command => command.Role == OverlayElementRole.TokenBar).Min(command => command.Bounds.Top));
        Assert.Equal(layout.Height, baseline.Bottom);
        Assert.Equal(appearance.GaugePaneWidth, Single(layout, OverlayElementRole.ManaTrack).Bounds.Right);
        Assert.Equal(appearance.GaugePaneWidth, Single(layout, OverlayElementRole.HpRefreshTrack).Bounds.Right);
        Assert.Equal(expectedRefreshHeight, Single(layout, OverlayElementRole.ManaRefreshTrack).Bounds.Height);
        Assert.Equal(expectedRefreshHeight, Single(layout, OverlayElementRole.HpRefreshTrack).Bounds.Height);
        Assert.Equal(1, baseline.Height);
        Assert.All(layout.Commands.Where(command => command.Role == OverlayElementRole.GraphGridDot), dot =>
        {
            Assert.Equal(1, dot.Bounds.Width);
            Assert.InRange(dot.Bounds.Height, 1, 2);
        });
        var firstGridColumn = layout.Commands.Where(command => command.Role == OverlayElementRole.GraphGridDot)
            .GroupBy(command => command.Bounds.Left).First().ToArray();
        Assert.Equal(4, firstGridColumn[1].Bounds.Top - firstGridColumn[0].Bounds.Top);
        Assert.Equal(4, layout.Commands.Count(command => command.Role == OverlayElementRole.ManaRefreshSeparator));
        Assert.Equal(6, layout.Commands.Count(command => command.Role == OverlayElementRole.HpRefreshSeparator));
        Assert.All(layout.Commands.Where(command => command.Role is OverlayElementRole.ManaRefreshSeparator or OverlayElementRole.HpRefreshSeparator),
            gap => Assert.Equal(expectedRefreshHeight, gap.Bounds.Width));
        Assert.True(Single(layout, OverlayElementRole.ManaText).FontSize >= 8.5 * scale);

        var capacity = TokenGraphViewport.CalculateVisibleBucketCount(appearance);
        var full = UsageOverlayRenderer.CreateLayout(SampleState() with { TokenBuckets = Enumerable.Repeat(1, capacity + 1).ToArray() },
            new OverlayPresentationSettings(settings.Colors, appearance), false);
        Assert.Equal(capacity, full.Commands.Count(command => command.Role == OverlayElementRole.TokenBar));
        // The formatted Settings value is covered separately; use the same resolved
        // viewport contract here to check that every reported bucket is drawable.
        Assert.Equal(TimeSpan.FromSeconds(capacity * 15), TokenGraphViewport.CalculateVisibleDuration(appearance));
        Assert.Equal(133, settings.Appearance.OverlayWidth);
    }

    [Fact]
    public void Horizontal_and_vertical_metrics_follow_their_own_monitor_scale()
    {
        var appearance = new EffectiveAppearanceSettings(266, 34, 96, 2, 0, 4, 2, 1);
        var layout = UsageOverlayRenderer.CreateLayout(SampleState(), new OverlayPresentationSettings(AppSettings.Default.Colors, appearance), true);

        Assert.Equal(4, Single(layout, OverlayElementRole.ManaTrack).Bounds.Left);
        Assert.Equal(0, Single(layout, OverlayElementRole.ManaTrack).Bounds.Top);
        Assert.Equal(1, Single(layout, OverlayElementRole.ManaRefreshTrack).Bounds.Height);
        var edges = layout.Commands.Where(command => command.Role == OverlayElementRole.OverlayPositionOutline).ToArray();
        Assert.Equal(2, edges[0].Bounds.Height);
        Assert.Equal(4, edges[2].Bounds.Width);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(1.25, 10)]
    [InlineData(1.5, 12)]
    [InlineData(2, 16)]
    public void Status_message_font_scales_with_dpi(double scale, int expectedFont)
    {
        var appearance = new EffectiveAppearanceSettings(266, 68, 96, 2, 0, 4, scale, scale);
        var layout = UsageOverlayRenderer.CreateLayout(SampleState() with { ContentMessage = "Loading…" },
            new OverlayPresentationSettings(AppSettings.Default.Colors, appearance), false);
        Assert.Equal(expectedFont, Single(layout, OverlayElementRole.ContentMessage).FontSize);
    }

    private static OverlayDrawCommand Single(UsageOverlayLayout layout, OverlayElementRole role) =>
        Assert.Single(layout.Commands, command => command.Role == role);

    private static OverlayPresentationSettings ReferencePhysicalSettings => new(
        AppSettings.Default.Colors,
        new EffectiveAppearanceSettings(288, 68, 100, 2, 0, 4, 2, 2));

    private static UsageOverlayState SampleState() => new(
        IsVisible: true,
        ManaBar: new GaugeDisplayState(75, 0.5, false),
        HpBar: new GaugeDisplayState(40, 0.25, false),
        TokenBuckets: [10_000, 25_000, 100_000],
        StatusStripeColor: null,
        StatusStripeTooltip: null);
}
