using CodexHp.Core.Positioning;
using Xunit;

namespace CodexHp.Core.Tests.Positioning;

public sealed class OverlayHostPlacementCalculatorTests
{
    private static readonly PhysicalRect PrimaryMonitor = new(0, 0, 3840, 2160);
    private static readonly PhysicalRect PrimaryTaskbar = new(0, 2064, 3840, 96);

    [Fact]
    public void Physical_rect_uses_half_open_containment_and_intersection_edges()
    {
        var rectangle = new PhysicalRect(10, 20, 30, 40);

        Assert.True(rectangle.Contains(new PhysicalRect(10, 20, 30, 40)));
        Assert.True(rectangle.Contains(new PhysicalRect(39, 59, 1, 1)));
        Assert.False(rectangle.Contains(new PhysicalRect(40, 59, 1, 1)));
        Assert.True(rectangle.IntersectsWith(new PhysicalRect(39, 59, 1, 1)));
        Assert.False(rectangle.IntersectsWith(new PhysicalRect(40, 20, 10, 10)));
        Assert.False(rectangle.IntersectsWith(new PhysicalRect(10, 60, 10, 10)));
    }

    [Theory]
    [InlineData(2090, OverlayHostMode.TaskbarChild, 2090)]
    [InlineData(1996, OverlayHostMode.DesktopPopup, 1996)]
    [InlineData(2012, OverlayHostMode.DesktopPopup, 1996)]
    [InlineData(2050, OverlayHostMode.TaskbarChild, 2078)]
    [InlineData(2043, OverlayHostMode.TaskbarChild, 2078)]
    public void Resolves_inside_outside_and_partial_overlap(
        int desiredTop,
        OverlayHostMode expectedMode,
        int expectedTop)
    {
        var result = OverlayHostPlacementCalculator.Resolve(
            new PhysicalRect(2, desiredTop, 288, 68),
            PrimaryMonitor,
            PrimaryTaskbar);

        Assert.Equal(expectedMode, result.Mode);
        Assert.Equal(new PhysicalRect(2, expectedTop, 288, 68), result.OverlayBounds);
    }

    [Theory]
    [InlineData(2029, OverlayHostMode.DesktopPopup, 1996)]
    [InlineData(2030, OverlayHostMode.TaskbarChild, 2078)]
    [InlineData(2031, OverlayHostMode.TaskbarChild, 2078)]
    public void Partial_overlap_uses_majority_area_and_centers_a_taskbar_snap(
        int desiredTop,
        OverlayHostMode expectedMode,
        int expectedTop)
    {
        var result = OverlayHostPlacementCalculator.Resolve(
            new PhysicalRect(2, desiredTop, 288, 68),
            PrimaryMonitor,
            PrimaryTaskbar);

        Assert.Equal(expectedMode, result.Mode);
        Assert.Equal(new PhysicalRect(2, expectedTop, 288, 68), result.OverlayBounds);
    }

    [Fact]
    public void Missing_taskbar_returns_a_monitor_clamped_desktop_popup()
    {
        var result = OverlayHostPlacementCalculator.Resolve(
            new PhysicalRect(-20, 2140, 288, 68),
            PrimaryMonitor,
            taskbarBounds: null);

        Assert.Equal(OverlayHostMode.DesktopPopup, result.Mode);
        Assert.Equal(new PhysicalRect(0, 2092, 288, 68), result.OverlayBounds);
    }

    [Fact]
    public void Taskbar_too_short_for_the_window_snaps_partial_overlap_outside()
    {
        var result = OverlayHostPlacementCalculator.Resolve(
            new PhysicalRect(2, 2090, 288, 68),
            PrimaryMonitor,
            new PhysicalRect(0, 2120, 3840, 40));

        Assert.Equal(OverlayHostMode.DesktopPopup, result.Mode);
        Assert.Equal(new PhysicalRect(2, 2052, 288, 68), result.OverlayBounds);
    }

    [Fact]
    public void Taskbar_child_is_clamped_horizontally_inside_the_monitor_and_taskbar()
    {
        var result = OverlayHostPlacementCalculator.Resolve(
            new PhysicalRect(3800, 2090, 288, 68),
            PrimaryMonitor,
            PrimaryTaskbar);

        Assert.Equal(OverlayHostMode.TaskbarChild, result.Mode);
        Assert.Equal(new PhysicalRect(3552, 2090, 288, 68), result.OverlayBounds);
    }

    [Fact]
    public void Negative_secondary_monitor_coordinates_are_preserved()
    {
        var monitor = new PhysicalRect(-1920, 0, 1920, 1080);
        var taskbar = new PhysicalRect(-1920, 1000, 1920, 80);

        var result = OverlayHostPlacementCalculator.Resolve(
            new PhysicalRect(-2000, 1012, 288, 68),
            monitor,
            taskbar);

        Assert.Equal(OverlayHostMode.TaskbarChild, result.Mode);
        Assert.Equal(new PhysicalRect(-1920, 1012, 288, 68), result.OverlayBounds);
    }
}
