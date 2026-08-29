using CodexHp.Core.Positioning;

namespace CodexHp.App.Tests.Presentation;

internal static class WindowsGuiTestGeometry
{
    public const int PreferredOverlayHeight = 68;

    private const int TaskbarEdgeInset = 2;

    public static int GetTaskbarCompatibleOverlayHeight(PhysicalRect taskbarBounds)
    {
        var availableHeight = taskbarBounds.Height - (TaskbarEdgeInset * 2);
        if (availableHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(taskbarBounds),
                taskbarBounds,
                "The taskbar must have room for the test overlay and its edge insets.");
        }

        return Math.Min(PreferredOverlayHeight, availableHeight);
    }
}
