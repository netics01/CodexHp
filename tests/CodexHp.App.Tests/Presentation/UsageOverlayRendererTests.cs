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

        var bars = UsageOverlayRenderer.CreateLayout(state, ReferencePhysicalSettings, false)
            .Commands
            .Where(command => command.Role == OverlayElementRole.TokenBar)
            .ToArray();

        Assert.Equal(3, bars.Length);
        Assert.Equal(280, bars[0].Bounds.Left);
        Assert.Equal(278, bars[1].Bounds.Left);
        Assert.Equal(276, bars[2].Bounds.Left);
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
                ReferencePhysicalSettings,
                false)
            .Commands
            .Where(command => command.Role == OverlayElementRole.TokenBar)
            .ToArray();

        Assert.Equal(58, bars[0].Bounds.Height);
        Assert.Equal(10, bars[1].Bounds.Height);
    }

    [Fact]
    public void Five_minute_grid_uses_twenty_fifteen_second_buckets()
    {
        var dots = UsageOverlayRenderer.CreateLayout(State([1]), ReferencePhysicalSettings, false)
            .Commands
            .Where(command => command.Role == OverlayElementRole.GraphGridDot)
            .ToArray();

        Assert.Contains(dots, dot => dot.Bounds.Left == 242);
        Assert.Contains(dots, dot => dot.Bounds.Left == 202);
        Assert.Contains(dots, dot => dot.Bounds.Left == 162);
        Assert.DoesNotContain(dots, dot => dot.Bounds.Left == 82);
    }

    [Fact]
    public void Missing_usage_is_rendered_as_placeholder_text()
    {
        var state = new UsageOverlayState(
            true,
            new GaugeDisplayState(null, 0, false),
            new GaugeDisplayState(null, 0, false),
            [],
            null,
            null);

        var texts = UsageOverlayRenderer.CreateLayout(state, ReferencePhysicalSettings, false)
            .Commands
            .Where(command => command.Role is OverlayElementRole.ManaText or OverlayElementRole.HpText)
            .ToArray();

        Assert.Equal(["--%", "--%"], texts.Select(command => command.Text));
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
        var commands = UsageOverlayRenderer.CreateLayout(State([1]), ReferencePhysicalSettings, true).Commands;
        var outline = commands.Where(command => command.Role == OverlayElementRole.OverlayPositionOutline).ToArray();

        Assert.Equal(4, outline.Length);
        Assert.Equal(new LayoutRect(0, 0, 288, 4), outline[0].Bounds);
        Assert.Equal(new LayoutRect(0, 64, 288, 4), outline[1].Bounds);
        Assert.Equal(new LayoutRect(0, 4, 4, 60), outline[2].Bounds);
        Assert.Equal(new LayoutRect(284, 4, 4, 60), outline[3].Bounds);
        Assert.All(commands.TakeLast(4), command => Assert.Equal(OverlayElementRole.OverlayPositionOutline, command.Role));
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
}
