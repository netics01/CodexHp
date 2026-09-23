using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class ResetCreditsRendererTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(3)]
    public void Banked_mode_fits_both_themes_and_preserves_weekly_graph_geometry(double scale)
    {
        foreach (var light in new[] { false, true })
        {
            var state = new UsageOverlayState(true, new(100, .8, false), new(70, .5, false), [1, 2, 3], null, null)
            { ServiceHealth = ServiceHealthState.Issue };
            var appearance = new EffectiveAppearanceSettings(
                P(130), P(32), P(48), P(1), 0, P(2), scale, scale);
            int P(double dip) => OverlayPixelPolicy.ToPixels(dip, scale);
            var settings = new OverlayPresentationSettings(light ? ColorSettings.LightDefault : ColorSettings.Default, appearance, light);
            var original = UsageOverlayRenderer.CreateLayout(state, settings, false);
            var current = UsageOverlayRenderer.CreateLayout(state with
            { BankedResets = new("3", "11d", false, false, "Banked resets: 3") }, settings, false);
            var text = Assert.Single(current.Commands, item => item.Role == OverlayElementRole.ResetCreditText);
            Assert.Equal("3 · 11d", text.Text);
            Assert.True(GdiUsageOverlayPainter.MeasureTextWidth(text.Text!, text.FontSize) <= text.Bounds.Width);
            Assert.DoesNotContain(current.Commands, item => item.Role.ToString().StartsWith("Mana", StringComparison.Ordinal));
            Assert.Equal(original.Commands.Where(Unchanged), current.Commands.Where(Unchanged));
            var fills = current.Commands.Where(item => item.Role == OverlayElementRole.ResetCreditIconFill).ToArray();
            Assert.NotEmpty(fills);
            Assert.All(fills, fill => Assert.Equal(settings.Colors.HpBar, fill.Color));
            var outline = current.Commands.Where(item => item.Role == OverlayElementRole.ResetCreditIcon).ToArray();
            var left = outline.Min(item => item.Bounds.Left);
            var right = outline.Max(item => item.Bounds.Right);
            Assert.All(fills, fill => Assert.True(fill.Bounds.Left > left && fill.Bounds.Right < right));
            var bitmap = GdiBitmapSourceRenderer.Render(current);
            Assert.Equal(P(130), bitmap.PixelWidth);
            Assert.Equal(P(32), bitmap.PixelHeight);
        }

        static bool Unchanged(OverlayDrawCommand command) => command.Role is
            OverlayElementRole.HpTrack or OverlayElementRole.HpFill or OverlayElementRole.HpText
            or OverlayElementRole.HpRefreshTrack or OverlayElementRole.HpRefreshFill or OverlayElementRole.HpRefreshSeparator
            or OverlayElementRole.GraphBaseline or OverlayElementRole.GraphGridDot or OverlayElementRole.TokenBar;
    }

    [Fact]
    public void Ticket_fill_follows_custom_HP_color_and_stale_opacity()
    {
        var state = new UsageOverlayState(true, new(100, 1, false), new(70, .5, false), [], null, null)
        { BankedResets = new("3", "11d", false, true, "Banked resets: 3") };
        var settings = AppSettings.Default with { Colors = ColorSettings.Default with { HpBar = ColorValue.Parse("#23AB45") } };
        var layout = UsageOverlayRenderer.CreateLayout(state, settings, false);
        var text = Assert.Single(layout.Commands, item => item.Role == OverlayElementRole.ResetCreditText);
        var fills = layout.Commands.Where(item => item.Role == OverlayElementRole.ResetCreditIconFill).ToArray();
        Assert.NotEmpty(fills);
        Assert.All(fills, fill =>
        {
            Assert.Equal(settings.Colors.HpBar, fill.Color);
            Assert.Equal(text.Opacity, fill.Opacity);
        });
    }

    [Fact]
    public void Urgency_highlights_only_expiry_and_stale_inventory_remains_dimmed()
    {
        var state = new UsageOverlayState(true, new(100, 1, true), new(70, .5, true), [], null, null)
        { BankedResets = new("3", "18h", true, true, "Banked resets: 3") };
        var layout = UsageOverlayRenderer.CreateLayout(state, AppSettings.Default, false);
        var text = Assert.Single(layout.Commands, item => item.Role == OverlayElementRole.ResetCreditText);
        var expiry = Assert.Single(layout.Commands, item => item.Role == OverlayElementRole.ResetCreditExpiryText);
        Assert.NotEqual(text.Color, expiry.Color);
        Assert.True(expiry.ClipBounds!.Value.Left > text.Bounds.Left);
        Assert.Equal(text.Bounds.Right, expiry.ClipBounds.Value.Right);
        Assert.Equal(text.Opacity, expiry.Opacity);
        Assert.True(text.Opacity < 1);
    }

    [Fact]
    public void Narrow_pane_prioritizes_count_without_shrinking_the_font()
    {
        var state = new UsageOverlayState(true, new(100, 1, false), new(70, .5, false), [], null, null)
        { BankedResets = new("3", "11d", false, false, "Banked resets: 3") };
        var settings = AppSettings.Default with { Appearance = AppSettings.Default.Appearance with { GaugePaneWidth = 20 } };
        var layout = UsageOverlayRenderer.CreateLayout(state, settings, false);
        var text = Assert.Single(layout.Commands, item => item.Role == OverlayElementRole.ResetCreditText);
        Assert.Equal("3", text.Text);
        Assert.True(text.FontSize >= Math.Ceiling(OverlayPixelPolicy.MinimumGaugeFontDip));
    }
}
