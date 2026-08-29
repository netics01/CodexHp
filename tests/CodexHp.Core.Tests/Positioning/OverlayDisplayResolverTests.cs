using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Positioning;

public sealed class OverlayDisplayResolverTests
{
    [Fact]
    public void Logical_appearance_scales_to_the_target_monitor_and_uses_its_taskbar()
    {
        var monitor = Monitor(
            "DISPLAY2",
            "MONITOR-STABLE-2",
            new PhysicalRect(0, 0, 3840, 2160),
            new PhysicalRect(0, 0, 3840, 2064),
            scale: 2,
            isPrimary: true);
        var taskbar = new PhysicalRect(0, 2064, 3840, 96);
        var settings = AppSettings.Default with
        {
            Appearance = new AppearanceSettings(144, 34, 50, 1, 0, 2),
        };

        var result = OverlayDisplayResolver.Resolve(
            settings,
            [new DisplayEnvironment(monitor, taskbar)]);

        Assert.Equal(
            new EffectiveAppearanceSettings(288, 68, 100, 2, 0, 4),
            result.Appearance);
        Assert.Equal("DISPLAY2", result.Placement.MonitorId);
        Assert.Equal(new PhysicalRect(4, 2078, 288, 68), result.Placement.Bounds);
        Assert.Equal(OverlayPlacementTarget.Taskbar, result.EffectiveTarget);
        Assert.False(result.WasSizeAdjusted);
    }

    [Fact]
    public void Taskbar_target_shrinks_to_a_thin_taskbar_without_becoming_unreadable()
    {
        var monitor = Monitor(
            "DISPLAY1",
            "MONITOR-STABLE-1",
            new PhysicalRect(0, 0, 1920, 1080),
            new PhysicalRect(0, 0, 1920, 1032),
            scale: 1,
            isPrimary: true);
        var settings = AppSettings.Default with
        {
            Appearance = new AppearanceSettings(144, 68, 50, 1, 0, 2),
        };

        var result = OverlayDisplayResolver.Resolve(
            settings,
            [new DisplayEnvironment(monitor, new PhysicalRect(0, 1032, 1920, 48))]);

        Assert.Equal(44, result.Appearance.OverlayHeight);
        Assert.Equal(new PhysicalRect(2, 1034, 144, 44), result.Placement.Bounds);
        Assert.Equal(OverlayPlacementTarget.Taskbar, result.EffectiveTarget);
        Assert.True(result.WasSizeAdjusted);
    }

    [Fact]
    public void Oversized_desktop_preference_is_effectively_clamped_instead_of_throwing()
    {
        var monitor = Monitor(
            "DISPLAY1",
            "MONITOR-STABLE-1",
            new PhysicalRect(0, 0, 1280, 720),
            new PhysicalRect(0, 0, 1280, 672),
            scale: 1,
            isPrimary: true);
        var settings = AppSettings.Default with
        {
            Appearance = new AppearanceSettings(4096, 512, 100, 2, 0, 4),
            Location = new OverlayLocationSettings(
                "DISPLAY1",
                0,
                0,
                "MONITOR-STABLE-1",
                OverlayPlacementTarget.Desktop,
                1,
                1),
        };

        var result = OverlayDisplayResolver.Resolve(
            settings,
            [new DisplayEnvironment(monitor, new PhysicalRect(0, 672, 1280, 48))]);

        Assert.Equal(1280, result.Appearance.OverlayWidth);
        Assert.Equal(512, result.Appearance.OverlayHeight);
        Assert.Equal(new PhysicalRect(0, 160, 1280, 512), result.Placement.Bounds);
        Assert.Equal(OverlayPlacementTarget.Desktop, result.EffectiveTarget);
        Assert.True(result.WasSizeAdjusted);
    }

    [Fact]
    public void Persistent_monitor_key_and_normalized_position_survive_device_name_changes()
    {
        var primary = Monitor(
            "DISPLAY1",
            "PRIMARY-KEY",
            new PhysicalRect(0, 0, 1920, 1080),
            new PhysicalRect(0, 0, 1920, 1032),
            scale: 1,
            isPrimary: true);
        var secondary = Monitor(
            "DISPLAY9",
            "SECONDARY-KEY",
            new PhysicalRect(-2560, 0, 2560, 1440),
            new PhysicalRect(-2560, 0, 2560, 1400),
            scale: 1.5,
            isPrimary: false);
        var settings = AppSettings.Default with
        {
            Appearance = new AppearanceSettings(200, 40, 50, 1, 0, 2),
            Location = new OverlayLocationSettings(
                "REMOVED-DISPLAY",
                0,
                0,
                "SECONDARY-KEY",
                OverlayPlacementTarget.Desktop,
                1,
                0.5),
        };

        var result = OverlayDisplayResolver.Resolve(
            settings,
            [
                new DisplayEnvironment(primary, new PhysicalRect(0, 1032, 1920, 48)),
                new DisplayEnvironment(secondary, new PhysicalRect(-2560, 1400, 2560, 40)),
            ]);

        Assert.Equal("DISPLAY9", result.Placement.MonitorId);
        Assert.Equal(new PhysicalRect(-300, 670, 300, 60), result.Placement.Bounds);
    }

    [Fact]
    public void Capture_records_persistent_monitor_target_and_normalized_coordinates()
    {
        var monitor = Monitor(
            "DISPLAY9",
            "SECONDARY-KEY",
            new PhysicalRect(-2560, 0, 2560, 1440),
            new PhysicalRect(-2560, 0, 2560, 1400),
            scale: 1.5,
            isPrimary: false);
        var taskbar = new PhysicalRect(-2560, 1400, 2560, 40);
        var bounds = new PhysicalRect(-2300, 1404, 300, 32);

        var location = OverlayDisplayResolver.Capture(
            bounds,
            [new DisplayEnvironment(monitor, taskbar)]);

        Assert.Equal("DISPLAY9", location.MonitorId);
        Assert.Equal("SECONDARY-KEY", location.MonitorKey);
        Assert.Equal(OverlayPlacementTarget.Taskbar, location.Target);
        Assert.Equal(260d / 2260d, location.NormalizedX!.Value, 6);
        Assert.Equal(0.5, location.NormalizedY!.Value, 6);
    }

    private static MonitorGeometry Monitor(
        string id,
        string persistentId,
        PhysicalRect bounds,
        PhysicalRect workArea,
        double scale,
        bool isPrimary) =>
        new(id, bounds, workArea, scale, scale, isPrimary, persistentId);
}
