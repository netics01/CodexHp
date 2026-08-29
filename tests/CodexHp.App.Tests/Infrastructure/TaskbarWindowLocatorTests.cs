using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class TaskbarWindowLocatorTests
{
    [Fact]
    public void Finds_the_primary_taskbar_on_the_primary_monitor()
    {
        var monitor = new WindowsMonitorService().GetMonitors().Single(item => item.IsPrimary);
        var result = new TaskbarWindowLocator().FindForMonitor(monitor.Id);

        Assert.NotNull(result);
        Assert.NotEqual(nint.Zero, result.Value.WindowHandle);
        Assert.Equal(monitor.Id, result.Value.MonitorId, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.Value.TaskbarBounds.Width > 0);
        Assert.True(result.Value.TaskbarBounds.Height > 0);
        Assert.True(result.Value.Dpi > 0);
    }

    [Fact]
    public void Finds_taskbar_by_a_screen_rectangle_on_the_primary_monitor()
    {
        var monitor = new WindowsMonitorService().GetMonitors().Single(item => item.IsPrimary);
        var desired = new CodexHp.Core.Positioning.PhysicalRect(
            monitor.Bounds.Left,
            monitor.Bounds.Bottom - 1,
            1,
            1);

        var result = new TaskbarWindowLocator().FindForOverlayBounds(desired);

        Assert.NotNull(result);
        Assert.Equal(monitor.Id, result.Value.MonitorId, StringComparer.OrdinalIgnoreCase);
    }
}
