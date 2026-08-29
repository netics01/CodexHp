using CodexHp.App.Presentation;
using CodexHp.Core.Settings;
using System.Windows.Media;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class GdiBitmapSourceRendererTests
{
    [Fact]
    public void Bitmap_surface_is_top_down_32_bit_rgb()
    {
        var bitmapInfo = GdiBitmapSourceRenderer.CreateBitmapInfo(288, 68);

        Assert.Equal(288, bitmapInfo.Header.Width);
        Assert.Equal(-68, bitmapInfo.Header.Height);
        Assert.Equal((ushort)1, bitmapInfo.Header.Planes);
        Assert.Equal((ushort)32, bitmapInfo.Header.BitCount);
        Assert.Equal(0u, bitmapInfo.Header.Compression);
    }

    [Fact]
    public void Rendered_WPF_bitmap_has_the_physical_layout_dimensions()
    {
        var layout = new UsageOverlayLayout(
            288,
            68,
            [
                new OverlayDrawCommand(
                    OverlayDrawKind.Rectangle,
                    OverlayElementRole.Background,
                    new LayoutRect(0, 0, 288, 68),
                    ColorValue.Parse("#18181C")),
            ]);

        var bitmap = GdiBitmapSourceRenderer.Render(layout);

        Assert.Equal(288, bitmap.PixelWidth);
        Assert.Equal(68, bitmap.PixelHeight);
        Assert.Equal(PixelFormats.Bgr32, bitmap.Format);
        Assert.True(bitmap.IsFrozen);
    }
}
