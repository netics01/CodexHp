using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class CustomOverlayTooltipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_fixed_widths_keep_ordinary_rows_on_one_line_and_wrap_incidents(bool light) => StaTest.Run(() =>
    {
        var normal = new OverlayTooltipView([new(null, [new("Week resets in", "6d 23h 59m")])], light);
        var compact = normal.MeasurePreferredSize(320);
        Assert.Equal(200, compact.Width);
        AssertRowsAreSingleLine(normal, compact);
        var shortCountdown = new OverlayTooltipView([new(null, [new("Week resets in", "1m")])], light);
        Assert.Equal(compact.Width, shortCountdown.MeasurePreferredSize(320).Width);
        var credits = new OverlayTooltipView([new("Banked resets", [new("Available", "2"), new("Expires", "2026-09-26 18:30")])], light);
        var creditSize = credits.MeasurePreferredSize(320);
        Assert.Equal(200, creditSize.Width);
        AssertRowsAreSingleLine(credits, creditSize);
        var shortIncident = new OverlayTooltipView([new("OpenAI service issue", [new("", "APIs")], true)], light);
        Assert.Equal(320, shortIncident.MeasurePreferredSize(1000).Width);
        var incident = new OverlayTooltipView([new("OpenAI service issue", [new("", string.Join(", ", Enumerable.Repeat("Affected service", 30)))], true)], light);
        var wide = incident.MeasurePreferredSize(320);
        Assert.Equal(320, wide.Width);
        Assert.True(wide.Height > compact.Height);
        var narrow = incident.MeasurePreferredSize(180);
        Assert.Equal(180, narrow.Width);
        Assert.True(narrow.Height > wide.Height);
        // Repeated measurements restore the selected fixed width after a narrow screen.
        Assert.Equal(wide, incident.MeasurePreferredSize(320));
        Assert.Equal(compact, normal.MeasurePreferredSize(320));
    });

    private static void AssertRowsAreSingleLine(OverlayTooltipView view, Size size)
    {
        view.Arrange(new Rect(new Point(), size));
        view.UpdateLayout();
        var scroll = view.Children.OfType<ScrollViewer>().Single();
        var content = Assert.IsType<StackPanel>(scroll.Content);
        foreach (var row in content.Children.OfType<Grid>())
            foreach (var text in row.Children.OfType<TextBlock>())
                Assert.InRange(text.ActualHeight, 1, text.FontSize * 1.7);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(3)]
    public void Bottom_taskbar_popup_stays_in_work_area_and_does_not_cover_overlay(double scale)
    {
        var work = new PhysicalRect(-1920, -300, 1920, 1000);
        var anchor = new PhysicalRect(-1920, 712, (int)(130 * scale), (int)(32 * scale));
        var result = OverlayTooltipPlacement.Calculate(anchor, work, (int)(320 * scale), (int)(210 * scale), scale)!;
        Assert.Equal(TooltipTailEdge.Bottom, result.TailEdge);
        Assert.True(work.Contains(result.Bounds));
        Assert.False(result.Bounds.IntersectsWith(anchor));
        Assert.InRange(result.TailOffset, 0, result.Bounds.Width);
    }

    [Theory]
    [InlineData(0, 0, TooltipTailEdge.Top)]
    [InlineData(1660, 0, TooltipTailEdge.Top)]
    [InlineData(1660, 1048, TooltipTailEdge.Bottom)]
    [InlineData(800, 550, TooltipTailEdge.Bottom)]
    public void Screen_edges_flip_and_clamp_the_popup(int x, int y, object expected)
    {
        var area = new PhysicalRect(0, 0, 1920, 1080);
        var anchor = new PhysicalRect(x, y, 260, 32);
        var result = OverlayTooltipPlacement.Calculate(anchor, area, 320, 210, 1)!;
        Assert.Equal((TooltipTailEdge)expected, result.TailEdge);
        Assert.True(area.Contains(result.Bounds));
        Assert.False(result.Bounds.IntersectsWith(anchor));
    }

    [Fact]
    public void Long_content_uses_available_height_instead_of_covering_the_overlay()
    {
        var result = OverlayTooltipPlacement.Calculate(new(0, 1040, 260, 40), new(0, 0, 1920, 1040), 320, 5000, 1)!;
        Assert.Equal(1028, result.Bounds.Height);
        Assert.Equal(TooltipTailEdge.Bottom, result.TailEdge);
    }

    [Fact]
    public void Very_tall_overlay_uses_a_side_and_impossible_layout_is_not_shown()
    {
        var area = new PhysicalRect(0, 0, 800, 600);
        var anchor = new PhysicalRect(300, 0, 200, 600);
        var result = OverlayTooltipPlacement.Calculate(anchor, area, 320, 200, 1)!;
        Assert.False(result.Bounds.IntersectsWith(anchor));
        Assert.True(area.Contains(result.Bounds));
        Assert.Null(OverlayTooltipPlacement.Calculate(area, area, 320, 200, 1));
    }

    [Fact]
    public void Hover_delay_escape_grace_period_and_disabling_preserve_focus() => StaTest.Run(() =>
    {
        using var surface = new WpfOverlaySurface(260, 64, NoOpHook);
        surface.SetVisibility(true);
        using var tooltip = new CustomOverlayTooltip(surface.WindowHandle);
        tooltip.Update("Week resets in 3d 2h", [new(null, [new("Week resets in", "3d 2h")])]);
        var foreground = NativeMethods.GetForegroundWindow();
        tooltip.ProcessHover(true, false, false, 0);
        tooltip.ProcessHover(true, false, false, 199);
        Assert.False(tooltip.IsVisible);
        tooltip.ProcessHover(true, false, false, 200);
        Assert.True(tooltip.IsVisible);
        Assert.Equal(foreground, NativeMethods.GetForegroundWindow());
        Assert.Equal(new nint(3), NativeMethods.SendMessageW(tooltip.WindowHandle, 0x0021, 0, 0));
        tooltip.ProcessHover(false, false, false, 210);
        tooltip.ProcessHover(false, true, false, 400);
        Assert.True(tooltip.IsVisible);
        tooltip.ProcessHover(false, true, true, 410);
        Assert.False(tooltip.IsVisible);
        tooltip.ProcessHover(true, false, false, 800);
        Assert.False(tooltip.IsVisible);
        tooltip.ProcessHover(false, false, false, 900);
        tooltip.ProcessHover(true, false, false, 1000);
        tooltip.ProcessHover(true, false, false, 1200);
        Assert.True(tooltip.IsVisible);
        tooltip.ProcessHover(false, false, false, 1210);
        tooltip.ProcessHover(false, false, false, 1460);
        Assert.False(tooltip.IsVisible);
        tooltip.ShowAtAnchor();
        tooltip.Update(null);
        Assert.False(tooltip.IsEnabled);
        Assert.False(tooltip.IsVisible);
    });

    [Fact]
    public void Real_popup_tracks_each_monitor_DPI_and_hides_with_its_target() => StaTest.Run(() =>
    {
        var previousDpi = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        try
        {
            using var surface = new WpfOverlaySurface(260, 64, NoOpHook);
            surface.SetVisibility(true);
            using var tooltip = new CustomOverlayTooltip(surface.WindowHandle);
            foreach (var monitor in new WindowsMonitorService().GetMonitors())
            {
                NativeMethods.SetWindowPos(surface.WindowHandle, NativeMethods.HwndTopmost,
                    monitor.Bounds.Left, monitor.WorkArea.Bottom - 70, 260, 64, NativeMethods.SwpNoActivate);
                tooltip.Update("Week resets in 3d", [new(null, [new("Week resets in", "3d")])], monitor.IsPrimary);
                tooltip.ShowAtAnchor();
                Assert.True(tooltip.IsVisible);
                Assert.Equal(NativeMethods.GetDpiForWindow(surface.WindowHandle), NativeMethods.GetDpiForWindow(tooltip.WindowHandle));
                Assert.True(NativeMethods.GetWindowRect(tooltip.WindowHandle, out var nativeBounds));
                var bounds = new PhysicalRect(nativeBounds.Left, nativeBounds.Top,
                    nativeBounds.Right - nativeBounds.Left, nativeBounds.Bottom - nativeBounds.Top);
                Assert.True(monitor.WorkArea.Contains(bounds));
                Assert.True(bounds.Bottom < monitor.WorkArea.Bottom - 70);
            }
            surface.SetVisibility(false);
            tooltip.ShowAtAnchor();
            Assert.False(tooltip.IsVisible);
        }
        finally { NativeMethods.SetThreadDpiAwarenessContext(previousDpi); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void View_wraps_long_incidents_and_fits_normal_rows(bool light) => StaTest.Run(() =>
    {
        var view = new OverlayTooltipView([
            new(null, [new("Week resets in", "6d 23h 59m")]),
            new("Banked resets", [new("Available", "2"), new("Expires", "2026-09-26 18:30")]),
            new("OpenAI service issue", [new("", "ChatGPT - " + string.Join(", ", Enumerable.Repeat("An affected component", 30)))], true),
        ], light);
        view.Measure(new Size(320, double.PositiveInfinity));
        Assert.InRange(view.DesiredSize.Width, 1, 320);
        Assert.True(view.DesiredSize.Height > 250);
        view.Measure(new Size(320, 250));
        view.Arrange(new Rect(0, 0, 320, 250));
        view.UpdateLayout();
        Assert.Equal(250, view.ActualHeight);
    });

    private static nint NoOpHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled) => 0;
}
