using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

public sealed class OverlayPositionController
{
    private readonly IMonitorService monitorService;
    private readonly Func<string, PhysicalRect?> taskbarBounds;

    public OverlayPositionController(
        IMonitorService monitorService,
        Func<string, PhysicalRect?>? taskbarBounds = null)
    {
        this.monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        this.taskbarBounds = taskbarBounds ?? (_ => null);
    }

    public OverlayDisplayResolution Resolve(AppSettings settings) =>
        OverlayDisplayResolver.Resolve(settings, this.GetDisplays());

    public OverlayPlacement Restore(AppSettings settings) => this.Resolve(settings).Placement;

    public OverlayLocationSettings Capture(PhysicalRect overlayBounds) =>
        OverlayDisplayResolver.Capture(overlayBounds, this.GetDisplays());

    public IReadOnlyList<DisplayEnvironment> GetDisplays() =>
        this.monitorService.GetMonitors()
            .Select(monitor => new DisplayEnvironment(monitor, this.TryGetTaskbarBounds(monitor.Id)))
            .ToArray();

    private PhysicalRect? TryGetTaskbarBounds(string monitorId)
    {
        try
        {
            return this.taskbarBounds(monitorId);
        }
        catch
        {
            return null;
        }
    }
}
