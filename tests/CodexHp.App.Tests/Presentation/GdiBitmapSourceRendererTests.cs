using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using System.Windows.Media;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class GdiBitmapSourceRendererTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(2.5)]
    [InlineData(3)]
    public void Status_stripe_reaches_first_and_last_pixel_rows_in_both_themes(double scale)
    {
        foreach (var light in new[] { false, true })
        foreach (var health in new[] { ServiceHealthState.Issue, ServiceHealthState.Unknown })
        {
            var colors = light ? ColorSettings.LightDefault : ColorSettings.Default;
            var expected = health == ServiceHealthState.Issue ? colors.ServiceIssue : colors.ServiceUnknown;
            var state = new UsageOverlayState(true, new(75, .5, false), new(42, .5, false), [], null, null)
            { ServiceHealth = health };
            var appearance = new EffectiveAppearanceSettings(
                OverlayPixelPolicy.ToPixels(130, scale), OverlayPixelPolicy.ToPixels(32, scale),
                OverlayPixelPolicy.ToPixels(48, scale), OverlayPixelPolicy.ToPixels(1, scale), 0,
                OverlayPixelPolicy.ToPixels(2, scale), scale, scale);
            var layout = UsageOverlayRenderer.CreateLayout(state, new OverlayPresentationSettings(colors, appearance, light), false);
            var stripe = layout.Commands.Single(command => command.Role == OverlayElementRole.StatusStripe).Bounds;
            Assert.Equal(0, stripe.Top);
            Assert.Equal(layout.Height, stripe.Bottom);
            var bitmap = GdiBitmapSourceRenderer.Render(layout);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            foreach (var y in new[] { 0, layout.Height - 1 })
            for (var x = stripe.Left; x < stripe.Right; x++)
            {
                var offset = y * stride + x * 4;
                Assert.Equal(new byte[] { expected.Blue, expected.Green, expected.Red }, pixels[offset..(offset + 3)]);
            }
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1.0 / 7, 1)]
    [InlineData(0.2, 1)]
    [InlineData(0.5, 1)]
    [InlineData(1, 1)]
    [InlineData(0.5, 1.25)]
    [InlineData(0.5, 1.5)]
    [InlineData(0.5, 2)]
    [InlineData(0.5, 2.5)]
    [InlineData(0.5, 3)]
    public void Refresh_gaps_are_rendered_over_both_fill_and_track(double fraction, double scale)
    {
        var state = new UsageOverlayState(
            true,
            new GaugeDisplayState(75, fraction, false),
            new GaugeDisplayState(40, fraction, false),
            [], null, null);
        var settings = AppSettings.Default with
        {
            Appearance = new AppearanceSettings(266, 68, 96, 2, 0, 4),
            Colors = AppSettings.Default.Colors with { RefreshGauge = ColorValue.Parse("#80FF40") },
        };
        var appearance = new EffectiveAppearanceSettings(
            OverlayPixelPolicy.ToPixels(133, scale), OverlayPixelPolicy.ToPixels(34, scale),
            OverlayPixelPolicy.ToPixels(48, scale), OverlayPixelPolicy.ToPixels(1, scale), 0,
            OverlayPixelPolicy.ToPixels(2, scale), scale, scale);
        var layout = UsageOverlayRenderer.CreateLayout(state, new OverlayPresentationSettings(settings.Colors, appearance), false);
        var bitmap = GdiBitmapSourceRenderer.Render(layout);
        Assert.Equal(layout.Width, bitmap.PixelWidth);
        Assert.Equal(layout.Height, bitmap.PixelHeight);
        Assert.Equal(PixelFormats.Bgr32, bitmap.Format);
        Assert.True(bitmap.IsFrozen);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var baseline = layout.Commands.Single(command => command.Role == OverlayElementRole.GraphBaseline).Bounds;
        Assert.Equal(bitmap.PixelHeight - 1, baseline.Top);
        Assert.Equal(bitmap.PixelHeight, baseline.Bottom);
        for (var x = baseline.Left; x < baseline.Right; x++)
        {
            var offset = (baseline.Top * stride) + (x * 4);
            Assert.Equal(new byte[] { 255, 255, 255 }, pixels[offset..(offset + 3)]);
        }

        foreach (var role in new[] { OverlayElementRole.ManaRefreshTrack, OverlayElementRole.HpRefreshTrack })
        {
            var track = layout.Commands.Single(command => command.Role == role).Bounds;
            var separatorRole = role == OverlayElementRole.ManaRefreshTrack
                ? OverlayElementRole.ManaRefreshSeparator
                : OverlayElementRole.HpRefreshSeparator;
            var gaps = layout.Commands.Where(command => command.Role == separatorRole).ToArray();
            Assert.Equal(role == OverlayElementRole.ManaRefreshTrack ? 4 : 6, gaps.Length);
            for (var y = track.Top; y < track.Bottom; y++)
            for (var x = track.Left; x < track.Right; x++)
            {
                var isGap = gaps.Any(gap => x >= gap.Bounds.Left && x < gap.Bounds.Right);
                var expected = isGap ? new byte[] { 0x1C, 0x18, 0x18 }
                    : x - track.Left < (int)Math.Floor(track.Width * fraction) ? new byte[] { 0x40, 0xFF, 0x80 }
                    : new byte[] { 0x4E, 0x46, 0x44 };
                var offset = (y * stride) + (x * 4);
                Assert.Equal(expected, pixels[offset..(offset + 3)]);
            }
        }
    }

}
