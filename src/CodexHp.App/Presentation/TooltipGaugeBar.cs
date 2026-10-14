using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation;

// A small copy of an overlay gauge for tooltip rows: track, fill in the gauge color, and the
// remaining percent centered inside. The text color follows each side of the fill edge.
internal sealed class TooltipGaugeBar : FrameworkElement
{
    internal const double BarWidth = 44;
    internal const double BarHeight = 16;
    private const double FontSize = 11;

    internal TooltipGaugeBar(int remainingPercent, ColorValue fillColor, bool isLight)
    {
        this.RemainingPercent = Math.Clamp(remainingPercent, 0, 100);
        this.FillColor = fillColor;
        this.IsLight = isLight;
        this.Width = BarWidth;
        this.Height = BarHeight;
        this.SnapsToDevicePixels = true;
    }

    internal int RemainingPercent { get; }

    internal ColorValue FillColor { get; }

    internal bool IsLight { get; }

    internal ColorValue TrackColor => ColorValue.Parse(this.IsLight ? "#D9DEE5" : "#3E3E44");

    // Matches the overlay: white on dark gauges, the more readable of white or dark text on light ones.
    internal ColorValue FillTextColor => this.IsLight ? UsageOverlayRenderer.ReadableText(this.FillColor) : ColorValue.Parse("#FFFFFF");

    internal ColorValue TrackTextColor => ColorValue.Parse(this.IsLight ? "#243042" : "#FFFFFF");

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, BarWidth, BarHeight);
        var fillWidth = Math.Round(BarWidth * this.RemainingPercent / 100.0);
        var fillBounds = new Rect(0, 0, fillWidth, BarHeight);
        var trackBounds = new Rect(fillWidth, 0, BarWidth - fillWidth, BarHeight);
        drawingContext.DrawRectangle(Brush(this.TrackColor), null, bounds);
        drawingContext.DrawRectangle(Brush(this.FillColor), null, fillBounds);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Text(ColorValue color) => new(
            $"{this.RemainingPercent}%",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            FontSize,
            Brush(color),
            dpi);
        var sample = Text(this.FillTextColor);
        var origin = new Point((BarWidth - sample.Width) / 2, (BarHeight - sample.Height) / 2);
        foreach (var (clip, color) in new[] { (fillBounds, this.FillTextColor), (trackBounds, this.TrackTextColor) })
        {
            if (clip.Width <= 0) continue;
            drawingContext.PushClip(new RectangleGeometry(clip));
            drawingContext.DrawText(Text(color), origin);
            drawingContext.Pop();
        }
    }

    private static SolidColorBrush Brush(ColorValue color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
        brush.Freeze();
        return brush;
    }
}
