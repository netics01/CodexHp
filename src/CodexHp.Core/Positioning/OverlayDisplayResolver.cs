using CodexHp.Core.Settings;

namespace CodexHp.Core.Positioning;

public sealed record OverlayDisplayResolution(
    EffectiveAppearanceSettings Appearance,
    OverlayPlacement Placement,
    OverlayPlacementTarget EffectiveTarget,
    bool WasSizeAdjusted);

public static class OverlayDisplayResolver
{
    private const int EdgeInsetDip = 2;
    private const int MinimumReadableWidthDip = 120;
    private const int MinimumReadableHeightDip = 24;

    public static OverlayDisplayResolution Resolve(
        AppSettings settings,
        IReadOnlyList<DisplayEnvironment> displays)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(displays);

        var (display, matchedSavedMonitor) = SelectDisplay(settings.Location, displays);
        var monitor = display.Monitor;
        var preferred = Scale(settings.Appearance, monitor.ScaleX, monitor.ScaleY);
        var effectiveTarget = settings.Location.Target;
        var edgeInsetX = ScaleValue(EdgeInsetDip, monitor.ScaleX);
        var edgeInsetY = ScaleValue(EdgeInsetDip, monitor.ScaleY);
        var width = Math.Min(preferred.OverlayWidth, monitor.Bounds.Width);
        var height = Math.Min(preferred.OverlayHeight, monitor.Bounds.Height);
        var taskbar = IntersectTaskbar(display.TaskbarBounds, monitor.Bounds);

        if (effectiveTarget == OverlayPlacementTarget.Taskbar && taskbar is { } taskbarBounds)
        {
            var horizontal = taskbarBounds.Width >= taskbarBounds.Height;
            if (horizontal)
            {
                var availableHeight = Math.Max(0, taskbarBounds.Height - (edgeInsetY * 2));
                var minimumHeight = ScaleValue(MinimumReadableHeightDip, monitor.ScaleY);
                if (availableHeight >= minimumHeight)
                {
                    height = Math.Min(height, availableHeight);
                    width = Math.Min(width, Math.Max(1, taskbarBounds.Width - (edgeInsetX * 2)));
                }
                else
                {
                    effectiveTarget = OverlayPlacementTarget.Desktop;
                }
            }
            else
            {
                var availableWidth = Math.Max(0, taskbarBounds.Width - (edgeInsetX * 2));
                var minimumWidth = ScaleValue(MinimumReadableWidthDip, monitor.ScaleX);
                if (availableWidth >= minimumWidth)
                {
                    width = Math.Min(width, availableWidth);
                    height = Math.Min(height, Math.Max(1, taskbarBounds.Height - (edgeInsetY * 2)));
                }
                else
                {
                    effectiveTarget = OverlayPlacementTarget.Desktop;
                }
            }
        }
        else if (effectiveTarget == OverlayPlacementTarget.Taskbar)
        {
            effectiveTarget = OverlayPlacementTarget.Desktop;
        }

        var container = effectiveTarget == OverlayPlacementTarget.Taskbar && taskbar is { } usableTaskbar
            ? usableTaskbar
            : ValidWorkArea(monitor);
        width = Math.Clamp(width, 1, container.Width);
        height = Math.Clamp(height, 1, container.Height);
        var appearance = FitInternalAppearance(preferred, width, height, monitor.ScaleX);
        var bounds = Place(
            settings.Location,
            container,
            appearance.OverlayWidth,
            appearance.OverlayHeight,
            effectiveTarget,
            matchedSavedMonitor,
            edgeInsetX,
            edgeInsetY);
        var placement = new OverlayPlacement(
            monitor.Id,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            bounds.Left - monitor.Bounds.Left,
            bounds.Top - monitor.Bounds.Top);

        return new OverlayDisplayResolution(
            appearance,
            placement,
            effectiveTarget,
            appearance.OverlayWidth != preferred.OverlayWidth
                || appearance.OverlayHeight != preferred.OverlayHeight);
    }

    public static OverlayLocationSettings Capture(
        PhysicalRect overlayBounds,
        IReadOnlyList<DisplayEnvironment> displays)
    {
        if (overlayBounds.Width <= 0 || overlayBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlayBounds));
        }

        Validate(displays);
        var display = displays.FirstOrDefault(candidate => candidate.Monitor.Bounds.Contains(overlayBounds.Center))
            ?? displays
                .OrderBy(candidate => SquaredDistance(candidate.Monitor.Bounds, overlayBounds.Center))
                .ThenByDescending(candidate => candidate.Monitor.IsPrimary)
                .First();
        var taskbar = IntersectTaskbar(display.TaskbarBounds, display.Monitor.Bounds);
        var target = taskbar is { } taskbarBounds && taskbarBounds.Contains(overlayBounds.Center)
            ? OverlayPlacementTarget.Taskbar
            : OverlayPlacementTarget.Desktop;
        var container = target == OverlayPlacementTarget.Taskbar && taskbar is { } selectedTaskbar
            ? selectedTaskbar
            : ValidWorkArea(display.Monitor);
        var normalizedX = Normalize(
            overlayBounds.Left - container.Left,
            container.Width - overlayBounds.Width);
        var normalizedY = Normalize(
            overlayBounds.Top - container.Top,
            container.Height - overlayBounds.Height);

        return new OverlayLocationSettings(
            display.Monitor.Id,
            overlayBounds.Left - display.Monitor.Bounds.Left,
            overlayBounds.Top - display.Monitor.Bounds.Top,
            display.Monitor.PersistentId,
            target,
            normalizedX,
            normalizedY);
    }

    private static EffectiveAppearanceSettings Scale(
        AppearanceSettings appearance,
        double scaleX,
        double scaleY) =>
        new(
            ScaleValue(appearance.OverlayWidth, scaleX),
            ScaleValue(appearance.OverlayHeight, scaleY),
            ScaleValue(appearance.GaugePaneWidth, scaleX),
            ScaleValue(appearance.GraphBarWidth, scaleX),
            appearance.GraphBarGap == 0 ? 0 : ScaleValue(appearance.GraphBarGap, scaleX),
            ScaleValue(appearance.StatusStripeWidth, scaleX));

    private static EffectiveAppearanceSettings FitInternalAppearance(
        EffectiveAppearanceSettings appearance,
        int width,
        int height,
        double scaleX)
    {
        var minimumChartWidth = ScaleValue(20, scaleX);
        var maximumGaugeWidth = Math.Max(1, width - Math.Min(width - 1, minimumChartWidth));
        return appearance with
        {
            OverlayWidth = width,
            OverlayHeight = height,
            GaugePaneWidth = Math.Clamp(appearance.GaugePaneWidth, 1, maximumGaugeWidth),
            GraphBarWidth = Math.Clamp(appearance.GraphBarWidth, 1, Math.Max(1, width)),
            GraphBarGap = Math.Clamp(appearance.GraphBarGap, 0, Math.Max(0, width - 1)),
            StatusStripeWidth = Math.Clamp(appearance.StatusStripeWidth, 1, Math.Max(1, width)),
        };
    }

    private static PhysicalRect Place(
        OverlayLocationSettings location,
        PhysicalRect container,
        int width,
        int height,
        OverlayPlacementTarget target,
        bool matchedSavedMonitor,
        int edgeInsetX,
        int edgeInsetY)
    {
        var maximumLeftOffset = Math.Max(0, container.Width - width);
        var maximumTopOffset = Math.Max(0, container.Height - height);
        if (matchedSavedMonitor
            && location.NormalizedX is { } normalizedX
            && location.NormalizedY is { } normalizedY)
        {
            return new PhysicalRect(
                container.Left + ScaleNormalized(normalizedX, maximumLeftOffset),
                container.Top + ScaleNormalized(normalizedY, maximumTopOffset),
                width,
                height);
        }

        if (matchedSavedMonitor
            && location.NormalizedX is null
            && location.NormalizedY is null
            && (!string.IsNullOrWhiteSpace(location.MonitorId)
                || !string.IsNullOrWhiteSpace(location.MonitorKey)))
        {
            return new PhysicalRect(
                Math.Clamp(container.Left + location.X, container.Left, container.Left + maximumLeftOffset),
                Math.Clamp(container.Top + location.Y, container.Top, container.Top + maximumTopOffset),
                width,
                height);
        }

        if (target == OverlayPlacementTarget.Taskbar)
        {
            var horizontal = container.Width >= container.Height;
            return horizontal
                ? new PhysicalRect(
                    Math.Min(container.Left + edgeInsetX, container.Left + maximumLeftOffset),
                    container.Top + (maximumTopOffset / 2),
                    width,
                    height)
                : new PhysicalRect(
                    container.Left + (maximumLeftOffset / 2),
                    Math.Min(container.Top + edgeInsetY, container.Top + maximumTopOffset),
                    width,
                    height);
        }

        return new PhysicalRect(
            container.Left,
            container.Top + maximumTopOffset,
            width,
            height);
    }

    private static (DisplayEnvironment Display, bool MatchedSavedMonitor) SelectDisplay(
        OverlayLocationSettings location,
        IReadOnlyList<DisplayEnvironment> displays)
    {
        var selected = string.IsNullOrWhiteSpace(location.MonitorKey)
            ? null
            : displays.FirstOrDefault(candidate => string.Equals(
                candidate.Monitor.PersistentId,
                location.MonitorKey,
                StringComparison.OrdinalIgnoreCase));
        selected ??= string.IsNullOrWhiteSpace(location.MonitorId)
            ? null
            : displays.FirstOrDefault(candidate => string.Equals(
                candidate.Monitor.Id,
                location.MonitorId,
                StringComparison.OrdinalIgnoreCase));
        return selected is not null
            ? (selected, true)
            : (displays.FirstOrDefault(candidate => candidate.Monitor.IsPrimary) ?? displays[0], false);
    }

    private static PhysicalRect ValidWorkArea(MonitorGeometry monitor) =>
        monitor.WorkArea.Width > 0
            && monitor.WorkArea.Height > 0
            && monitor.Bounds.Contains(monitor.WorkArea)
                ? monitor.WorkArea
                : monitor.Bounds;

    private static PhysicalRect? IntersectTaskbar(PhysicalRect? taskbar, PhysicalRect monitor)
    {
        if (taskbar is not { } value)
        {
            return null;
        }

        var left = Math.Max(value.Left, monitor.Left);
        var top = Math.Max(value.Top, monitor.Top);
        var right = Math.Min(value.Right, monitor.Right);
        var bottom = Math.Min(value.Bottom, monitor.Bottom);
        return left < right && top < bottom
            ? new PhysicalRect(left, top, right - left, bottom - top)
            : null;
    }

    private static int ScaleValue(int logicalValue, double scale) =>
        Math.Max(1, (int)Math.Round(logicalValue * scale, MidpointRounding.AwayFromZero));

    private static int ScaleNormalized(double normalized, int maximumOffset) =>
        (int)Math.Round(
            Math.Clamp(normalized, 0, 1) * maximumOffset,
            MidpointRounding.AwayFromZero);

    private static double Normalize(int offset, int maximumOffset) =>
        maximumOffset <= 0
            ? 0
            : Math.Clamp((double)offset / maximumOffset, 0, 1);

    private static long SquaredDistance(PhysicalRect rectangle, PhysicalPoint point)
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

    private static void Validate(IReadOnlyList<DisplayEnvironment> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (displays.Count == 0
            || displays.Any(display =>
                string.IsNullOrWhiteSpace(display.Monitor.Id)
                || display.Monitor.Bounds.Width <= 0
                || display.Monitor.Bounds.Height <= 0
                || !double.IsFinite(display.Monitor.ScaleX)
                || !double.IsFinite(display.Monitor.ScaleY)
                || display.Monitor.ScaleX <= 0
                || display.Monitor.ScaleY <= 0))
        {
            throw new ArgumentException("At least one valid display environment is required.", nameof(displays));
        }
    }
}
