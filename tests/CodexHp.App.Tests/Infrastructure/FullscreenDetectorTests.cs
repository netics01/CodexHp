using CodexHp.App.Infrastructure;
using CodexHp.Core.Positioning;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class FullscreenDetectorTests
{
    private static readonly MonitorGeometry LeftMonitor = new(
        "left",
        new PhysicalRect(0, 0, 1920, 1080),
        new PhysicalRect(0, 0, 1920, 1040),
        1,
        1,
        true);

    [Fact]
    public void Visible_foreground_window_covering_the_same_monitor_is_fullscreen()
    {
        var snapshot = VisibleWindow(new PhysicalRect(0, 0, 1920, 1080), "left");

        Assert.True(FullscreenDetector.ShouldHideFor(snapshot, LeftMonitor, (nint)99));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void Hidden_minimized_cloaked_and_shell_windows_are_excluded(
        bool isVisible,
        bool isMinimized,
        bool isCloaked,
        bool isShellWindow)
    {
        var snapshot = VisibleWindow(new PhysicalRect(0, 0, 1920, 1080), "left") with
        {
            IsVisible = isVisible,
            IsMinimized = isMinimized,
            IsCloaked = isCloaked,
            IsShellWindow = isShellWindow
        };

        Assert.False(FullscreenDetector.ShouldHideFor(snapshot, LeftMonitor, (nint)99));
    }

    [Fact]
    public void CodexHp_own_window_is_excluded()
    {
        var snapshot = VisibleWindow(new PhysicalRect(0, 0, 1920, 1080), "left") with
        {
            Handle = (nint)99
        };

        Assert.False(FullscreenDetector.ShouldHideFor(snapshot, LeftMonitor, (nint)99));
    }

    [Fact]
    public void Fullscreen_window_on_another_monitor_does_not_hide_usage_overlay()
    {
        var snapshot = VisibleWindow(new PhysicalRect(1920, 0, 2560, 1440), "right");

        Assert.False(FullscreenDetector.ShouldHideFor(snapshot, LeftMonitor, (nint)99));
    }

    [Theory]
    [InlineData(0, 0, 1917, 1080)]
    [InlineData(0, 3, 1920, 1077)]
    [InlineData(10, 10, 1900, 1060)]
    public void Window_that_does_not_cover_monitor_bounds_is_not_fullscreen(
        int left,
        int top,
        int width,
        int height)
    {
        var snapshot = VisibleWindow(new PhysicalRect(left, top, width, height), "left");

        Assert.False(FullscreenDetector.ShouldHideFor(snapshot, LeftMonitor, (nint)99));
    }

    [Fact]
    public void Native_snapshot_absence_is_treated_as_not_fullscreen()
    {
        var detector = new FullscreenDetector(_ => null);

        Assert.False(detector.IsFullscreenOnMonitor((nint)99, LeftMonitor));
    }

    private static ForegroundWindowSnapshot VisibleWindow(PhysicalRect bounds, string monitorId) =>
        new(
            Handle: (nint)42,
            IsVisible: true,
            IsMinimized: false,
            IsCloaked: false,
            IsShellWindow: false,
            FrameBounds: bounds,
            MonitorId: monitorId);
}
