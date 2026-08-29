namespace CodexHp.Core.Settings;

public enum OverlayPlacementTarget
{
    Taskbar,
    Desktop,
}

public sealed record OverlayLocationSettings(
    string? MonitorId,
    int X,
    int Y,
    string? MonitorKey = null,
    OverlayPlacementTarget Target = OverlayPlacementTarget.Taskbar,
    double? NormalizedX = null,
    double? NormalizedY = null)
{
    public static OverlayLocationSettings Default { get; } = new(null, 0, 0);
}
