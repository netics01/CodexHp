namespace CodexHp.Core.Positioning;

public enum OverlayHostMode
{
    DesktopPopup,
    TaskbarChild,
}

public readonly record struct OverlayHostPlacement(
    OverlayHostMode Mode,
    PhysicalRect OverlayBounds);

public static class OverlayHostPlacementCalculator
{
    public static OverlayHostPlacement Resolve(
        PhysicalRect desiredOverlayBounds,
        PhysicalRect monitorBounds,
        PhysicalRect? taskbarBounds)
    {
        Validate(desiredOverlayBounds, monitorBounds);

        var requested = ClampInside(desiredOverlayBounds, monitorBounds);
        if (taskbarBounds is not { } rawTaskbar
            || !TryIntersect(rawTaskbar, monitorBounds, out var taskbar))
        {
            return new OverlayHostPlacement(OverlayHostMode.DesktopPopup, requested);
        }

        if (taskbar.Contains(requested))
        {
            return new OverlayHostPlacement(OverlayHostMode.TaskbarChild, requested);
        }

        if (!taskbar.IntersectsWith(requested))
        {
            return new OverlayHostPlacement(OverlayHostMode.DesktopPopup, requested);
        }

        var childCandidate = CanContain(taskbar, requested)
            ? CreateChildCandidate(requested, monitorBounds, taskbar)
            : (PhysicalRect?)null;
        var popupCandidate = FindNearestPopupCandidate(requested, monitorBounds, taskbar);

        if (popupCandidate is null)
        {
            if (childCandidate is { } onlyChild)
            {
                return new OverlayHostPlacement(OverlayHostMode.TaskbarChild, onlyChild);
            }

            return new OverlayHostPlacement(OverlayHostMode.DesktopPopup, requested);
        }

        if (childCandidate is null)
        {
            return new OverlayHostPlacement(OverlayHostMode.DesktopPopup, popupCandidate.Value);
        }

        var taskbarOverlapArea = IntersectionArea(requested, taskbar);
        var outsideTaskbarArea = ((long)requested.Width * requested.Height) - taskbarOverlapArea;

        return taskbarOverlapArea >= outsideTaskbarArea
            ? new OverlayHostPlacement(OverlayHostMode.TaskbarChild, childCandidate.Value)
            : new OverlayHostPlacement(OverlayHostMode.DesktopPopup, popupCandidate.Value);
    }

    private static PhysicalRect? FindNearestPopupCandidate(
        PhysicalRect requested,
        PhysicalRect monitor,
        PhysicalRect taskbar)
    {
        var regions = new[]
        {
            new PhysicalRect(monitor.Left, monitor.Top, monitor.Width, taskbar.Top - monitor.Top),
            new PhysicalRect(monitor.Left, taskbar.Bottom, monitor.Width, monitor.Bottom - taskbar.Bottom),
            new PhysicalRect(monitor.Left, monitor.Top, taskbar.Left - monitor.Left, monitor.Height),
            new PhysicalRect(taskbar.Right, monitor.Top, monitor.Right - taskbar.Right, monitor.Height),
        };

        PhysicalRect? nearest = null;
        var nearestDistance = long.MaxValue;

        foreach (var region in regions)
        {
            if (!CanContain(region, requested))
            {
                continue;
            }

            var candidate = ClampInside(requested, region);
            var distance = SquaredDistance(requested, candidate);
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private static PhysicalRect CreateChildCandidate(
        PhysicalRect requested,
        PhysicalRect monitor,
        PhysicalRect taskbar)
    {
        var candidate = ClampInside(requested, taskbar);

        if (taskbar.Bottom == monitor.Bottom)
        {
            return candidate with
            {
                Top = taskbar.Top + ((taskbar.Height - requested.Height) / 2),
            };
        }

        if (taskbar.Top == monitor.Top)
        {
            return candidate with
            {
                Top = taskbar.Top + ((taskbar.Height - requested.Height) / 2),
            };
        }

        if (taskbar.Right == monitor.Right)
        {
            return candidate with
            {
                Left = taskbar.Left + ((taskbar.Width - requested.Width) / 2),
            };
        }

        if (taskbar.Left == monitor.Left)
        {
            return candidate with
            {
                Left = taskbar.Left + ((taskbar.Width - requested.Width) / 2),
            };
        }

        return candidate;
    }

    private static long IntersectionArea(PhysicalRect first, PhysicalRect second) =>
        TryIntersect(first, second, out var intersection)
            ? (long)intersection.Width * intersection.Height
            : 0;

    private static bool TryIntersect(
        PhysicalRect first,
        PhysicalRect second,
        out PhysicalRect intersection)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);

        if (left >= right || top >= bottom)
        {
            intersection = default;
            return false;
        }

        intersection = new PhysicalRect(left, top, right - left, bottom - top);
        return true;
    }

    private static PhysicalRect ClampInside(PhysicalRect rectangle, PhysicalRect container)
    {
        var maximumLeft = container.Right - rectangle.Width;
        var maximumTop = container.Bottom - rectangle.Height;
        return rectangle with
        {
            Left = Math.Clamp(rectangle.Left, container.Left, maximumLeft),
            Top = Math.Clamp(rectangle.Top, container.Top, maximumTop),
        };
    }

    private static bool CanContain(PhysicalRect container, PhysicalRect rectangle) =>
        container.Width >= rectangle.Width && container.Height >= rectangle.Height;

    private static long SquaredDistance(PhysicalRect first, PhysicalRect second)
    {
        var deltaX = (long)first.Left - second.Left;
        var deltaY = (long)first.Top - second.Top;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    private static void Validate(PhysicalRect desiredOverlayBounds, PhysicalRect monitorBounds)
    {
        if (desiredOverlayBounds.Width <= 0 || desiredOverlayBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(desiredOverlayBounds));
        }

        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorBounds));
        }

        if (!CanContain(monitorBounds, desiredOverlayBounds))
        {
            throw new ArgumentException(
                "The desired window must fit inside the monitor.",
                nameof(desiredOverlayBounds));
        }
    }
}
