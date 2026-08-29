using CodexHp.Core.Settings;

namespace CodexHp.Core.Positioning;

public static class OverlayPlacementCalculator
{
    private const int DefaultLeftInset = 2;
    private const int DefaultBottomInset = 12;

    public static OverlayPlacement Restore(
        OverlayLocationSettings location,
        IReadOnlyList<MonitorGeometry> monitors,
        int physicalWidth,
        int physicalHeight)
    {
        ArgumentNullException.ThrowIfNull(location);
        Validate(monitors, physicalWidth, physicalHeight);

        var savedMonitor = string.IsNullOrWhiteSpace(location.MonitorId)
            ? null
            : monitors.FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                location.MonitorId,
                StringComparison.OrdinalIgnoreCase));
        var useDefaultPlacement = savedMonitor is null;
        var monitor = useDefaultPlacement ? GetPrimary(monitors) : savedMonitor!;

        var requestedLeft = useDefaultPlacement
            ? monitor.Bounds.Left + DefaultLeftInset
            : monitor.Bounds.Left + location.X;
        var requestedTop = useDefaultPlacement
            ? monitor.Bounds.Bottom
                - DefaultBottomInset
                - physicalHeight
            : monitor.Bounds.Top + location.Y;

        var maximumLeft = Math.Max(monitor.Bounds.Left, monitor.Bounds.Right - physicalWidth);
        var maximumTop = Math.Max(monitor.Bounds.Top, monitor.Bounds.Bottom - physicalHeight);
        var physicalLeft = Math.Clamp(requestedLeft, monitor.Bounds.Left, maximumLeft);
        var physicalTop = Math.Clamp(requestedTop, monitor.Bounds.Top, maximumTop);

        return new OverlayPlacement(
            monitor.Id,
            physicalLeft,
            physicalTop,
            physicalWidth,
            physicalHeight,
            physicalLeft - monitor.Bounds.Left,
            physicalTop - monitor.Bounds.Top);
    }

    public static OverlayLocationSettings Capture(
        PhysicalRect overlayBounds,
        IReadOnlyList<MonitorGeometry> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        var monitor = monitors.FirstOrDefault(candidate => candidate.Bounds.Contains(overlayBounds.Center))
            ?? SelectNearest(monitors, overlayBounds.Center);

        return new OverlayLocationSettings(
            monitor.Id,
            overlayBounds.Left - monitor.Bounds.Left,
            overlayBounds.Top - monitor.Bounds.Top);
    }

    private static void Validate(
        IReadOnlyList<MonitorGeometry> monitors,
        int physicalWidth,
        int physicalHeight)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        if (physicalWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalWidth));
        }

        if (physicalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalHeight));
        }

        if (monitors.Any(monitor =>
                string.IsNullOrWhiteSpace(monitor.Id)
                || monitor.Bounds.Width <= 0
                || monitor.Bounds.Height <= 0))
        {
            throw new ArgumentException("All monitor geometries must be valid.", nameof(monitors));
        }
    }

    private static MonitorGeometry GetPrimary(IReadOnlyList<MonitorGeometry> monitors) =>
        monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];

    private static MonitorGeometry SelectNearest(
        IReadOnlyList<MonitorGeometry> monitors,
        PhysicalPoint point) =>
        monitors
            .OrderBy(monitor => SquaredDistanceTo(monitor.Bounds, point))
            .ThenByDescending(monitor => monitor.IsPrimary)
            .First();

    private static long SquaredDistanceTo(PhysicalRect rectangle, PhysicalPoint point)
    {
        var horizontal = point.X < rectangle.Left
            ? (long)rectangle.Left - point.X
            : point.X >= rectangle.Right
                ? (long)point.X - rectangle.Right + 1
                : 0;
        var vertical = point.Y < rectangle.Top
            ? (long)rectangle.Top - point.Y
            : point.Y >= rectangle.Bottom
                ? (long)point.Y - rectangle.Bottom + 1
                : 0;

        return (horizontal * horizontal) + (vertical * vertical);
    }
}
