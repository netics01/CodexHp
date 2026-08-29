using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

public sealed class OverlayPositionController
{
    private readonly IMonitorService monitorService;

    public OverlayPositionController(IMonitorService monitorService)
    {
        this.monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
    }

    public OverlayPlacement Restore(AppSettings settings) =>
        OverlayPlacementCalculator.Restore(
            settings.Location,
            this.monitorService.GetMonitors(),
            settings.Appearance.OverlayWidth,
            settings.Appearance.OverlayHeight);

    public OverlayLocationSettings Capture(PhysicalRect overlayBounds) =>
        OverlayPlacementCalculator.Capture(overlayBounds, this.monitorService.GetMonitors());
}
