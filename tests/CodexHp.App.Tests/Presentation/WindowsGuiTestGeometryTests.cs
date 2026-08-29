using CodexHp.Core.Positioning;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class WindowsGuiTestGeometryTests
{
    [Theory]
    [InlineData(48, 44)]
    [InlineData(72, 68)]
    [InlineData(80, 68)]
    public void Taskbar_compatible_height_preserves_insets_without_exceeding_the_preferred_height(
        int taskbarHeight,
        int expectedOverlayHeight)
    {
        var taskbarBounds = new PhysicalRect(0, 768 - taskbarHeight, 1024, taskbarHeight);

        var overlayHeight = WindowsGuiTestGeometry.GetTaskbarCompatibleOverlayHeight(taskbarBounds);
        var requestedBounds = new PhysicalRect(
            taskbarBounds.Left + 2,
            taskbarBounds.Bottom - overlayHeight - 2,
            288,
            overlayHeight);
        var resolution = OverlayHostPlacementCalculator.Resolve(
            requestedBounds,
            new PhysicalRect(0, 0, 1024, 768),
            taskbarBounds);

        Assert.Equal(expectedOverlayHeight, overlayHeight);
        Assert.True(overlayHeight + 4 <= taskbarBounds.Height);
        Assert.True(overlayHeight <= WindowsGuiTestGeometry.PreferredOverlayHeight);
        Assert.Equal(OverlayHostMode.TaskbarChild, resolution.Mode);
    }
}
