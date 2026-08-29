using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class WindowsMonitorServiceTests
{
    [Fact]
    public void Current_windows_monitors_have_valid_geometry_and_scale()
    {
        var monitors = new WindowsMonitorService().GetMonitors();

        Assert.NotEmpty(monitors);
        Assert.Single(monitors, monitor => monitor.IsPrimary);
        Assert.Equal(monitors.Count, monitors.Select(monitor => monitor.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(monitors, monitor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(monitor.Id));
            Assert.False(string.IsNullOrWhiteSpace(monitor.PersistentId));
            Assert.True(monitor.Bounds.Width > 0);
            Assert.True(monitor.Bounds.Height > 0);
            Assert.True(monitor.WorkArea.Width > 0);
            Assert.True(monitor.WorkArea.Height > 0);
            Assert.True(monitor.ScaleX > 0);
            Assert.True(monitor.ScaleY > 0);
        });
    }
}
