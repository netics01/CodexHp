using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Positioning;

public sealed class OverlayPlacementCalculatorTests
{
    private static readonly MonitorGeometry Primary = new(
        "primary",
        new PhysicalRect(0, 0, 3840, 2160),
        new PhysicalRect(0, 0, 3840, 2064),
        2,
        2,
        true);

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Missing_location_uses_physical_product_default_at_every_display_scale(double scale)
    {
        var monitor = Primary with { ScaleX = scale, ScaleY = scale };

        var placement = OverlayPlacementCalculator.Restore(
            OverlayLocationSettings.Default,
            [monitor],
            288,
            68);

        Assert.Equal("primary", placement.MonitorId);
        Assert.Equal(2, placement.PhysicalLeft);
        Assert.Equal(2080, placement.PhysicalTop);
        Assert.Equal(288, placement.PhysicalWidth);
        Assert.Equal(68, placement.PhysicalHeight);
        Assert.Equal(2, placement.RelativeX);
        Assert.Equal(2080, placement.RelativeY);
    }

    [Fact]
    public void Restore_does_not_expose_a_development_comparison_placement_option()
    {
        var restore = typeof(OverlayPlacementCalculator).GetMethod(
            nameof(OverlayPlacementCalculator.Restore));

        Assert.NotNull(restore);
        Assert.DoesNotContain(
            restore.GetParameters(),
            parameter => parameter.ParameterType == typeof(bool));
    }

    [Fact]
    public void Saved_physical_offset_is_restored_without_target_monitor_scale()
    {
        var secondary = new MonitorGeometry(
            "secondary",
            new PhysicalRect(1920, 0, 2560, 1440),
            new PhysicalRect(1920, 0, 2560, 1400),
            1.5,
            1.5,
            false);

        var placement = OverlayPlacementCalculator.Restore(
            new OverlayLocationSettings("secondary", 100, 50),
            [Primary, secondary],
            288,
            68);

        Assert.Equal("secondary", placement.MonitorId);
        Assert.Equal(2020, placement.PhysicalLeft);
        Assert.Equal(50, placement.PhysicalTop);
        Assert.Equal(288, placement.PhysicalWidth);
        Assert.Equal(68, placement.PhysicalHeight);
        Assert.Equal(100, placement.RelativeX);
        Assert.Equal(50, placement.RelativeY);
    }

    [Fact]
    public void Missing_saved_monitor_falls_back_to_primary_product_default()
    {
        var secondary = new MonitorGeometry(
            "secondary",
            new PhysicalRect(3840, 0, 2560, 1440),
            new PhysicalRect(3840, 0, 2560, 1400),
            1.5,
            1.5,
            false);

        var placement = OverlayPlacementCalculator.Restore(
            new OverlayLocationSettings("removed", 25, 40),
            [secondary, Primary],
            288,
            68);

        Assert.Equal("primary", placement.MonitorId);
        Assert.Equal(2, placement.PhysicalLeft);
        Assert.Equal(2080, placement.PhysicalTop);
    }

    [Fact]
    public void Restored_window_is_clamped_inside_full_monitor_including_taskbar_area()
    {
        var reduced = new MonitorGeometry(
            "primary",
            new PhysicalRect(0, 0, 900, 700),
            new PhysicalRect(0, 0, 900, 600),
            1.5,
            1.5,
            true);

        var placement = OverlayPlacementCalculator.Restore(
            new OverlayLocationSettings("primary", 1000, 800),
            [reduced],
            288,
            68);

        Assert.Equal(612, placement.PhysicalLeft);
        Assert.Equal(632, placement.PhysicalTop);
        Assert.Equal(612, placement.RelativeX);
        Assert.Equal(632, placement.RelativeY);
    }

    [Fact]
    public void Capturing_a_physical_position_stores_monitor_relative_physical_coordinates()
    {
        var secondary = new MonitorGeometry(
            "secondary",
            new PhysicalRect(-1600, 0, 1600, 900),
            new PhysicalRect(-1600, 0, 1600, 860),
            1.25,
            1.25,
            false);

        var location = OverlayPlacementCalculator.Capture(
            new PhysicalRect(-1475, 250, 288, 68),
            [Primary, secondary]);

        Assert.Equal(new OverlayLocationSettings("secondary", 125, 250), location);
    }
}
