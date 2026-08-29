using CodexHp.App.Infrastructure;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation;

internal static class GdiUsageOverlayPainter
{
    private static readonly ColorValue Background = ColorValue.Parse("#18181C");
    private static readonly nint InvalidGraphicObject = new(-1);

    internal static void Paint(nint deviceContext, UsageOverlayLayout layout)
    {
        if (deviceContext == nint.Zero)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(layout);
        foreach (var command in layout.Commands)
        {
            var color = Blend(command.Color, Background, command.Opacity);
            if (command.Kind == OverlayDrawKind.Rectangle)
            {
                FillRectangle(deviceContext, command.Bounds, color);
            }
            else
            {
                DrawText(deviceContext, command, color);
            }
        }
    }

    internal static uint ToColorRef(ColorValue color) =>
        color.Red | ((uint)color.Green << 8) | ((uint)color.Blue << 16);

    internal static ColorValue Blend(
        ColorValue foreground,
        ColorValue background,
        double opacity)
    {
        var alpha = Math.Clamp(opacity, 0, 1);
        return new ColorValue(
            BlendComponent(foreground.Red, background.Red, alpha),
            BlendComponent(foreground.Green, background.Green, alpha),
            BlendComponent(foreground.Blue, background.Blue, alpha));
    }

    private static byte BlendComponent(byte foreground, byte background, double opacity) =>
        checked((byte)Math.Round(
            (foreground * opacity) + (background * (1 - opacity)),
            MidpointRounding.AwayFromZero));

    private static void FillRectangle(
        nint deviceContext,
        LayoutRect bounds,
        ColorValue color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var brush = NativeMethods.CreateSolidBrush(ToColorRef(color));
        if (brush == nint.Zero)
        {
            return;
        }

        try
        {
            var rectangle = ToNativeRect(bounds);
            _ = NativeMethods.FillRect(deviceContext, ref rectangle, brush);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private static void DrawText(
        nint deviceContext,
        OverlayDrawCommand command,
        ColorValue color)
    {
        if (command.Bounds.Width <= 0 || command.Bounds.Height <= 0)
        {
            return;
        }

        _ = NativeMethods.SetBkMode(deviceContext, NativeMethods.TransparentBackgroundMode);
        _ = NativeMethods.SetTextColor(deviceContext, ToColorRef(color));
        var font = NativeMethods.CreateFontW(
            -Math.Max(1, command.FontSize),
            0,
            0,
            0,
            NativeMethods.FontWeightSemiBold,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            "Segoe UI Variable Text");
        var previousFont = font == nint.Zero
            ? nint.Zero
            : NativeMethods.SelectObject(deviceContext, font);

        try
        {
            var rectangle = ToNativeRect(command.Bounds);
            _ = NativeMethods.DrawTextW(
                deviceContext,
                command.Text ?? string.Empty,
                -1,
                ref rectangle,
                NativeMethods.DrawTextCenter
                    | NativeMethods.DrawTextVerticalCenter
                    | NativeMethods.DrawTextSingleLine);
        }
        finally
        {
            if (previousFont != nint.Zero && previousFont != InvalidGraphicObject)
            {
                _ = NativeMethods.SelectObject(deviceContext, previousFont);
            }

            if (font != nint.Zero)
            {
                _ = NativeMethods.DeleteObject(font);
            }
        }
    }

    private static NativeMethods.NativeRect ToNativeRect(LayoutRect bounds) => new()
    {
        Left = bounds.Left,
        Top = bounds.Top,
        Right = bounds.Right,
        Bottom = bounds.Bottom,
    };
}
