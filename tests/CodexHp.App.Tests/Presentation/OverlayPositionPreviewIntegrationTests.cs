using CodexHp.App.Application;
using CodexHp.App.Presentation;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class OverlayPositionPreviewIntegrationTests
{
    private static readonly MonitorGeometry Primary = new(
        "DISPLAY1",
        new PhysicalRect(0, 0, 1920, 1080),
        new PhysicalRect(0, 0, 1920, 1040),
        1,
        1,
        true);

    private static readonly MonitorGeometry Secondary = new(
        "DISPLAY2",
        new PhysicalRect(1920, 0, 2560, 1440),
        new PhysicalRect(1920, 0, 2560, 1400),
        1.5,
        1.5,
        false);

    [Theory]
    [InlineData(true, 0x0201u, true)]
    [InlineData(false, 0x0201u, false)]
    [InlineData(true, 0x0203u, false)]
    [InlineData(true, 0x0202u, false)]
    public void Drag_starts_only_for_left_button_down_in_position_mode(
        bool positionMode,
        uint message,
        bool expected)
    {
        Assert.Equal(expected, UsageOverlayWindow.CanBeginDrag(positionMode, message));
    }

    [Fact]
    public void Moving_to_another_monitor_updates_relative_physical_location_and_cancel_restores_baseline()
    {
        var monitorService = new FakeMonitorService([Primary, Secondary]);
        var positionController = new OverlayPositionController(monitorService);
        var baseline = AppSettings.Default with
        {
            Location = new OverlayLocationSettings("DISPLAY1", 12, 20),
        };
        var previews = new List<AppSettings>();
        var viewModel = new SettingsWindowViewModel(
            baseline,
            previews.Add,
            _ => { },
            settings => settings);

        var captured = positionController.Capture(new PhysicalRect(2070, 150, 288, 68));
        viewModel.PreviewLocation(captured);

        Assert.Equal("DISPLAY2", viewModel.Working.Location.MonitorId);
        Assert.Equal(150, viewModel.Working.Location.X);
        Assert.Equal(150, viewModel.Working.Location.Y);
        Assert.Equal(OverlayPlacementTarget.Desktop, viewModel.Working.Location.Target);
        Assert.NotNull(viewModel.Working.Location.NormalizedX);
        Assert.NotNull(viewModel.Working.Location.NormalizedY);
        Assert.Equal(viewModel.Working.Location, previews[^1].Location);

        viewModel.Cancel();
        var restored = positionController.Restore(previews[^1]);

        Assert.Equal("DISPLAY1", restored.MonitorId);
        Assert.Equal(12, restored.PhysicalLeft);
        Assert.Equal(20, restored.PhysicalTop);
    }

    [Fact]
    public void Resolve_returns_one_effective_physical_contract_for_rendering_and_placement()
    {
        var monitor = Primary with
        {
            ScaleX = 2,
            ScaleY = 2,
            PersistentId = "STABLE-DISPLAY-1",
        };
        var taskbar = new PhysicalRect(0, 1024, 1920, 56);
        var controller = new OverlayPositionController(
            new FakeMonitorService([monitor]),
            _ => taskbar);

        var resolution = controller.Resolve(AppSettings.Default);

        Assert.Equal(new EffectiveAppearanceSettings(288, 48, 100, 2, 0, 4), resolution.Appearance);
        Assert.Equal(resolution.Appearance.OverlayWidth, resolution.Placement.PhysicalWidth);
        Assert.Equal(resolution.Appearance.OverlayHeight, resolution.Placement.PhysicalHeight);
        Assert.Equal(OverlayPlacementTarget.Taskbar, resolution.EffectiveTarget);
        Assert.True(resolution.WasSizeAdjusted);
    }

    private sealed class FakeMonitorService(IReadOnlyList<MonitorGeometry> monitors) : IMonitorService
    {
        public IReadOnlyList<MonitorGeometry> GetMonitors() => monitors;

        public MonitorGeometry? GetMonitorForWindow(nint windowHandle) => monitors[0];
    }
}
