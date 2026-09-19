using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class UsageOverlayRendererTests
{
    [Fact]
    public void Token_bars_are_drawn_newest_first_from_the_right()
    {
        var state = State([10_000, 55_000, 100_000]);

        var bars = UsageOverlayRenderer.CreateLayout(state, ReferencePresentation, false)
            .Commands
            .Where(command => command.Role == OverlayElementRole.TokenBar)
            .ToArray();

        Assert.Equal(3, bars.Length);
        Assert.Equal(286, bars[0].Bounds.Left);
        Assert.Equal(284, bars[1].Bounds.Left);
        Assert.Equal(282, bars[2].Bounds.Left);
        Assert.Equal(AppSettings.Default.Colors.TokenHigh, bars[0].Color);
        Assert.Equal(AppSettings.Default.Colors.TokenLow, bars[2].Color);
        Assert.True(bars[0].Bounds.Height > bars[1].Bounds.Height);
        Assert.True(bars[1].Bounds.Height > bars[2].Bounds.Height);
    }

    [Fact]
    public void Token_bars_use_soft_log_height_against_the_visible_maximum()
    {
        var bars = UsageOverlayRenderer.CreateLayout(
                State([3_677, 45_172]),
                ReferencePresentation,
                false)
            .Commands
            .Where(command => command.Role == OverlayElementRole.TokenBar)
            .ToArray();

        Assert.Equal(67, bars[0].Bounds.Height);
        Assert.Equal(12, bars[1].Bounds.Height);
    }

    [Fact]
    public void Five_minute_grid_uses_twenty_fifteen_second_buckets()
    {
        var dots = UsageOverlayRenderer.CreateLayout(State([1]), ReferencePresentation, false)
            .Commands
            .Where(command => command.Role == OverlayElementRole.GraphGridDot)
            .ToArray();

        Assert.Contains(dots, dot => dot.Bounds.Left == 248);
        Assert.Contains(dots, dot => dot.Bounds.Left == 208);
        Assert.Contains(dots, dot => dot.Bounds.Left == 168);
        Assert.DoesNotContain(dots, dot => dot.Bounds.Left == 82);
    }

    [Fact]
    public void Loading_usage_is_rendered_as_one_full_overlay_message()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Waiting,
            ServiceHealthState.Issue,
            "Partial System Degradation",
            new VisibilityState(false, false),
            AppSettings.Default,
            nowUnixMs: 1_000_000);

        var commands = UsageOverlayRenderer.CreateLayout(state, ReferencePhysicalSettings, false).Commands;
        var texts = commands
            .Where(command => command.Kind == OverlayDrawKind.Text)
            .ToArray();

        Assert.Equal("Loading…", Assert.Single(texts).Text);
        Assert.Contains(
            commands,
            command => command.Role == OverlayElementRole.StatusStripe);
    }

    [Fact]
    public void Stale_usage_commands_use_reduced_opacity()
    {
        var stale = State([1]) with
        {
            ManaBar = new GaugeDisplayState(75, 0.5, true),
            HpBar = new GaugeDisplayState(40, 0.25, true),
        };

        var layout = UsageOverlayRenderer.CreateLayout(stale, ReferencePhysicalSettings, false);

        Assert.Equal(0.55, layout.Commands.First(command => command.Role == OverlayElementRole.ManaFill).Opacity);
        Assert.Equal(0.55, layout.Commands.First(command => command.Role == OverlayElementRole.ManaText).Opacity);
        Assert.Equal(0.55, layout.Commands.First(command => command.Role == OverlayElementRole.HpRefreshFill).Opacity);
        Assert.Equal(1, layout.Commands.First(command => command.Role == OverlayElementRole.TokenBar).Opacity);
    }

    [Fact]
    public void Overlay_position_change_mode_adds_four_inside_four_pixel_outline_edges_last()
    {
        var commands = UsageOverlayRenderer.CreateLayout(State([1]), ReferencePresentation, true).Commands;
        var outline = commands.Where(command => command.Role == OverlayElementRole.OverlayPositionOutline).ToArray();

        Assert.Equal(4, outline.Length);
        Assert.Equal(new LayoutRect(0, 0, 288, 4), outline[0].Bounds);
        Assert.Equal(new LayoutRect(0, 64, 288, 4), outline[1].Bounds);
        Assert.Equal(new LayoutRect(0, 4, 4, 60), outline[2].Bounds);
        Assert.Equal(new LayoutRect(284, 4, 4, 60), outline[3].Bounds);
        Assert.All(commands.TakeLast(4), command => Assert.Equal(OverlayElementRole.OverlayPositionOutline, command.Role));
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, false)]
    [InlineData(28, false)]
    [InlineData(50, false)]
    [InlineData(96, true)]
    [InlineData(100, false)]
    [InlineData(150, true)]
    public void Weekly_refresh_has_six_background_gaps_at_day_boundaries(int paneWidth, bool stale)
    {
        var state = State([]) with { HpBar = new GaugeDisplayState(40, 0.5, stale) };
        var settings = ReferencePhysicalSettings with
        {
            Appearance = ReferencePhysicalSettings.Appearance with { GaugePaneWidth = paneWidth },
        };
        var commands = UsageOverlayRenderer.CreateLayout(state, settings, false).Commands;
        var track = commands.Single(command => command.Role == OverlayElementRole.HpRefreshTrack).Bounds;
        var fill = commands.Single(command => command.Role == OverlayElementRole.HpRefreshFill);
        var gaps = commands.Where(command => command.Role == OverlayElementRole.HpRefreshSeparator).ToArray();

        Assert.Equal(6, gaps.Length);
        Assert.Equal(track.Width / 2, fill.Bounds.Width);
        Assert.Equal(stale ? 0.55 : 1, fill.Opacity);
        var previousRight = track.Left;
        for (var index = 0; index < gaps.Length; index++)
        {
            var gap = gaps[index];
            var position = track.Width * (index + 1) / 7.0;
            var boundary = gap.Bounds.Width == 1
                ? (int)Math.Floor(position)
                : (int)Math.Round(position, MidpointRounding.AwayFromZero);
            Assert.Equal(track.Left + boundary - (gap.Bounds.Width / 2), gap.Bounds.Left);
            Assert.True(gap.Bounds.Left > previousRight);
            Assert.Equal(track.Top, gap.Bounds.Top);
            Assert.Equal(track.Height, gap.Bounds.Height);
            Assert.Equal(ColorValue.Parse("#18181C"), gap.Color);
            Assert.Equal(1, gap.Opacity);
            previousRight = gap.Bounds.Right;
        }

        Assert.True(previousRight < track.Right);
        Assert.Single(commands, command => command.Role == OverlayElementRole.ManaRefreshFill);
    }

    [Fact]
    public void Extremely_narrow_weekly_track_with_status_stripe_remains_visible()
    {
        var state = State([]) with { StatusStripeColor = ColorValue.Parse("#FF8800") };
        var settings = ReferencePhysicalSettings with
        {
            Appearance = ReferencePhysicalSettings.Appearance with { GaugePaneWidth = 20, StatusStripeWidth = 10 },
        };
        var commands = UsageOverlayRenderer.CreateLayout(state, settings, false).Commands;

        Assert.DoesNotContain(commands, command => command.Role == OverlayElementRole.HpRefreshSeparator);
        Assert.DoesNotContain(commands, command => command.Role == OverlayElementRole.ManaRefreshSeparator);
        Assert.True(commands.Single(command => command.Role == OverlayElementRole.HpRefreshTrack).Bounds.Width > 0);
    }

    [Theory]
    [InlineData(20, 0, false)]
    [InlineData(21, 0, false)]
    [InlineData(22, 0, false)]
    [InlineData(50, 0, false)]
    [InlineData(96, 0, true)]
    [InlineData(100, 4, false)]
    [InlineData(22, 4, false)]
    public void Five_hour_refresh_has_four_gaps_at_hour_boundaries(int paneWidth, int stripeWidth, bool stale)
    {
        var state = State([]) with
        {
            ManaBar = new GaugeDisplayState(75, 0.5, stale),
            StatusStripeColor = stripeWidth > 0 ? ColorValue.Parse("#FF8800") : null,
        };
        var settings = ReferencePhysicalSettings with
        {
            Appearance = ReferencePhysicalSettings.Appearance with
            {
                GaugePaneWidth = paneWidth,
                StatusStripeWidth = stripeWidth,
            },
        };
        var commands = UsageOverlayRenderer.CreateLayout(state, settings, false).Commands;
        var track = commands.Single(command => command.Role == OverlayElementRole.ManaRefreshTrack).Bounds;
        var fill = commands.Single(command => command.Role == OverlayElementRole.ManaRefreshFill);
        var gaps = commands.Where(command => command.Role == OverlayElementRole.ManaRefreshSeparator).ToArray();

        Assert.Equal(4, gaps.Length);
        Assert.Equal(track.Width / 2, fill.Bounds.Width);
        Assert.Equal(stale ? 0.55 : 1, fill.Opacity);
        var previousRight = track.Left;
        for (var index = 0; index < gaps.Length; index++)
        {
            var gap = gaps[index];
            var position = track.Width * (index + 1) / 5.0;
            var boundary = gap.Bounds.Width == 1
                ? (int)Math.Floor(position)
                : (int)Math.Round(position, MidpointRounding.AwayFromZero);
            Assert.Equal(track.Left + boundary - (gap.Bounds.Width / 2), gap.Bounds.Left);
            Assert.True(gap.Bounds.Left > previousRight);
            Assert.Equal(track.Top, gap.Bounds.Top);
            Assert.Equal(track.Height, gap.Bounds.Height);
            Assert.Equal(ColorValue.Parse("#18181C"), gap.Color);
            Assert.Equal(1, gap.Opacity);
            previousRight = gap.Bounds.Right;
        }

        Assert.True(previousRight < track.Right);
    }

    private static UsageOverlayState State(IReadOnlyList<int> buckets) => new(
        IsVisible: true,
        ManaBar: new GaugeDisplayState(75, 0.5, false),
        HpBar: new GaugeDisplayState(40, 0.25, false),
        TokenBuckets: buckets,
        StatusStripeColor: null,
        StatusStripeTooltip: null);

    private static AppSettings ReferencePhysicalSettings => AppSettings.Default with
    {
        Appearance = new AppearanceSettings(288, 68, 100, 2, 0, 4),
    };

    private static OverlayPresentationSettings ReferencePresentation => new(
        AppSettings.Default.Colors,
        new EffectiveAppearanceSettings(288, 68, 100, 2, 0, 4, 2, 2));
}
