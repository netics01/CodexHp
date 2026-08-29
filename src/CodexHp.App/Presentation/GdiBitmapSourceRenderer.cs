using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexHp.App.Infrastructure;

namespace CodexHp.App.Presentation;

internal static class GdiBitmapSourceRenderer
{
    private static readonly nint InvalidGraphicObject = new(-1);

    internal static BitmapSource Render(UsageOverlayLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Width <= 0 || layout.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layout), "The usage overlay layout must have positive dimensions.");
        }

        var screenDeviceContext = NativeMethods.GetDC(nint.Zero);
        if (screenDeviceContext == nint.Zero)
        {
            throw new InvalidOperationException("The screen device context is unavailable.");
        }

        nint memoryDeviceContext = nint.Zero;
        nint bitmap = nint.Zero;
        nint previousBitmap = nint.Zero;
        try
        {
            memoryDeviceContext = NativeMethods.CreateCompatibleDC(screenDeviceContext);
            if (memoryDeviceContext == nint.Zero)
            {
                throw new InvalidOperationException("The memory device context could not be created.");
            }

            var bitmapInfo = CreateBitmapInfo(layout.Width, layout.Height);
            bitmap = NativeMethods.CreateDIBSection(
                screenDeviceContext,
                ref bitmapInfo,
                NativeMethods.DibRgbColors,
                out _,
                nint.Zero,
                0);
            if (bitmap == nint.Zero)
            {
                throw new InvalidOperationException("The GDI overlay bitmap could not be created.");
            }

            previousBitmap = NativeMethods.SelectObject(memoryDeviceContext, bitmap);
            if (previousBitmap == nint.Zero || previousBitmap == InvalidGraphicObject)
            {
                throw new InvalidOperationException("The GDI overlay bitmap could not be selected.");
            }

            GdiUsageOverlayPainter.Paint(memoryDeviceContext, layout);
            var sourceWithUnusedAlpha = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            var source = new FormatConvertedBitmap(
                sourceWithUnusedAlpha,
                PixelFormats.Bgr32,
                destinationPalette: null,
                alphaThreshold: 0);
            source.Freeze();
            return source;
        }
        finally
        {
            if (previousBitmap != nint.Zero && previousBitmap != InvalidGraphicObject)
            {
                _ = NativeMethods.SelectObject(memoryDeviceContext, previousBitmap);
            }

            if (bitmap != nint.Zero)
            {
                _ = NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDeviceContext != nint.Zero)
            {
                _ = NativeMethods.DeleteDC(memoryDeviceContext);
            }

            _ = NativeMethods.ReleaseDC(nint.Zero, screenDeviceContext);
        }
    }

    internal static NativeMethods.BitmapInfo CreateBitmapInfo(int width, int height) => new()
    {
        Header = new NativeMethods.BitmapInfoHeader
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>()),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        },
    };
}
