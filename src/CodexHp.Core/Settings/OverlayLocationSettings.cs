namespace CodexHp.Core.Settings;

public sealed record OverlayLocationSettings(string? MonitorId, int X, int Y)
{
    public static OverlayLocationSettings Default { get; } = new(null, 0, 0);
}
