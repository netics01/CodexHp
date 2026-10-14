using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation;

internal sealed class OverlayTooltipView : Grid
{
    private readonly BalloonChrome chrome;
    private readonly ScrollViewer scroll;
    private readonly double preferredWidth;

    internal OverlayTooltipView(IReadOnlyList<OverlayTooltipSection> sections, bool isLight, ColorSettings? gaugeColors = null)
    {
        this.preferredWidth = sections.Any(section => section.IsWarning) ? 320 : 200;
        var background = Brush(isLight ? "#F8FAFC" : "#18212D");
        var border = Brush(isLight ? "#BCC8D6" : "#405064");
        var foreground = Brush(isLight ? "#15273B" : "#F1F5FB");
        var secondary = Brush(isLight ? "#4C6076" : "#AABBD0");
        var accent = Brush(isLight ? "#12629C" : "#80C5FF");
        var warning = Brush(isLight ? "#915000" : "#FFC477");
        this.chrome = new BalloonChrome(background, border);
        this.Children.Add(this.chrome);
        var content = new StackPanel();
        foreach (var section in sections)
        {
            if (content.Children.Count > 0)
                content.Children.Add(new Border { Height = 1, Background = border, Margin = new(0, 11, 0, 11) });
            if (!string.IsNullOrWhiteSpace(section.Title))
                content.Children.Add(Text(section.Title, section.IsWarning ? warning : foreground, 13, true, new(0, 0, 0, 6)));
            foreach (var row in section.Rows)
            {
                if (string.IsNullOrEmpty(row.Label))
                {
                    content.Children.Add(Text(row.Value, section.IsWarning ? warning : secondary, 12, false, new(0, 3, 0, 3)));
                    continue;
                }
                var grid = new Grid { Margin = new(0, 3, 0, 3) };
                // Labels are short and never break mid-word; a long value wraps within its own column instead.
                grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                var label = Text(row.Label, secondary, 12, false, new(0, 0, 8, 0));
                label.TextWrapping = TextWrapping.NoWrap;
                grid.Children.Add(label);
                if (row.Gauges is { Count: > 0 } gauges && gaugeColors is not null)
                {
                    grid.Children.Add(Gauges(gauges, row.GaugeNote, gaugeColors, isLight, warning));
                    content.Children.Add(grid);
                    continue;
                }
                var value = Text(row.Value, row.Label == "Expires" ? foreground : accent, 13, true, new());
                value.TextAlignment = TextAlignment.Right;
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);
                content.Children.Add(grid);
            }
        }
        this.scroll = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
            PanningMode = PanningMode.VerticalOnly,
        };
        this.Children.Add(this.scroll);
        this.SetTail(TooltipTailEdge.Bottom, 40);
        this.UseLayoutRounding = true;
        this.SnapsToDevicePixels = true;
    }

    internal Size MeasurePreferredSize(double maximumWidth)
    {
        // Two stable widths: ordinary account details and service incidents.
        // Only an unusually narrow monitor work area may constrain the chosen width.
        var width = Math.Max(1, Math.Min(maximumWidth, this.preferredWidth));
        this.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, this.DesiredSize.Height);
    }

    internal void SetTail(TooltipTailEdge edge, double offset)
    {
        this.chrome.Edge = edge;
        this.chrome.Offset = offset;
        this.chrome.InvalidateVisual();
        this.scroll.Margin = edge switch
        {
            TooltipTailEdge.Top => new(16, 21, 16, 13),
            TooltipTailEdge.Bottom => new(16, 13, 16, 21),
            TooltipTailEdge.Left => new(24, 13, 16, 13),
            _ => new(16, 13, 24, 13),
        };
    }

    // Mini gauges in the overlay colors, right-aligned, with an optional note underneath.
    private static StackPanel Gauges(
        IReadOnlyList<TooltipGauge> gauges,
        string? note,
        ColorSettings colors,
        bool isLight,
        Brush noteColor)
    {
        var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var gauge in gauges)
        {
            var color = gauge.Kind == TooltipGaugeKind.FiveHour ? colors.ManaBar : colors.HpBar;
            bars.Children.Add(new TooltipGaugeBar(gauge.RemainingPercent, color, isLight)
            {
                Margin = new(bars.Children.Count == 0 ? 0 : 4, 0, 0, 0),
            });
        }

        var panel = new StackPanel();
        Grid.SetColumn(panel, 1);
        panel.Children.Add(bars);
        if (!string.IsNullOrEmpty(note))
        {
            var noteText = Text(note, noteColor, 12, false, new(0, 4, 0, 0));
            noteText.TextAlignment = TextAlignment.Right;
            panel.Children.Add(noteText);
        }

        return panel;
    }

    private static TextBlock Text(string value, Brush color, double size, bool bold, Thickness margin) => new()
    {
        Text = value, Foreground = color, FontFamily = new("Segoe UI"), FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, Margin = margin,
    };

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private sealed class BalloonChrome(Brush fill, Brush stroke) : FrameworkElement
    {
        internal TooltipTailEdge Edge { get; set; }
        internal double Offset { get; set; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            const double tail = 8;
            var rect = new Rect(.5 + (this.Edge == TooltipTailEdge.Left ? tail : 0),
                .5 + (this.Edge == TooltipTailEdge.Top ? tail : 0),
                Math.Max(0, this.ActualWidth - 1 - (this.Edge is TooltipTailEdge.Left or TooltipTailEdge.Right ? tail : 0)),
                Math.Max(0, this.ActualHeight - 1 - (this.Edge is TooltipTailEdge.Top or TooltipTailEdge.Bottom ? tail : 0)));
            if (rect.Width <= 0 || rect.Height <= 0) return;
            var offset = this.Offset;
            Point a, b, tip;
            switch (this.Edge)
            {
                case TooltipTailEdge.Top:
                    a = new(offset - tail, rect.Top + 1); b = new(offset + tail, rect.Top + 1); tip = new(offset, .5); break;
                case TooltipTailEdge.Bottom:
                    a = new(offset - tail, rect.Bottom - 1); b = new(offset + tail, rect.Bottom - 1); tip = new(offset, this.ActualHeight - .5); break;
                case TooltipTailEdge.Left:
                    a = new(rect.Left + 1, offset - tail); b = new(rect.Left + 1, offset + tail); tip = new(.5, offset); break;
                default:
                    a = new(rect.Right - 1, offset - tail); b = new(rect.Right - 1, offset + tail); tip = new(this.ActualWidth - .5, offset); break;
            }
            var triangle = new StreamGeometry();
            using (var context = triangle.Open())
            {
                context.BeginFigure(a, true, true);
                context.LineTo(tip, true, false);
                context.LineTo(b, true, false);
            }
            var shape = Geometry.Combine(new RectangleGeometry(rect, 10, 10), triangle, GeometryCombineMode.Union, null);
            drawingContext.DrawGeometry(fill, new Pen(stroke, 1), shape);
        }
    }
}
