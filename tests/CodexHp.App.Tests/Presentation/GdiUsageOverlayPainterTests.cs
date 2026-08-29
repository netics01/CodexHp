using CodexHp.App.Presentation;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class GdiUsageOverlayPainterTests
{
    [Fact]
    public void Color_ref_uses_windows_bgr_layout()
    {
        var colorRef = GdiUsageOverlayPainter.ToColorRef(ColorValue.Parse("#112233"));

        Assert.Equal(0x00332211u, colorRef);
    }

    [Fact]
    public void Stale_color_is_preblended_with_opaque_background()
    {
        var blended = GdiUsageOverlayPainter.Blend(
            ColorValue.Parse("#3A8EFF"),
            ColorValue.Parse("#18181C"),
            0.55);

        Assert.Equal(ColorValue.Parse("#2B5999"), blended);
    }

    [Theory]
    [InlineData(-1, "#18181C")]
    [InlineData(0, "#18181C")]
    [InlineData(1, "#3A8EFF")]
    [InlineData(2, "#3A8EFF")]
    public void Blend_clamps_opacity(double opacity, string expected)
    {
        var blended = GdiUsageOverlayPainter.Blend(
            ColorValue.Parse("#3A8EFF"),
            ColorValue.Parse("#18181C"),
            opacity);

        Assert.Equal(ColorValue.Parse(expected), blended);
    }
}
