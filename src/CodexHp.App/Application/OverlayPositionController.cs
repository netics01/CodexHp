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

    public OverlayPlacement Restore(
        AppSettings settings,
        bool useDevelopmentComparisonPlacement = false) =>
        OverlayPlacementCalculator.Restore(
            settings.Location,
            this.monitorService.GetMonitors(),
            settings.Appearance.OverlayWidth,
            settings.Appearance.OverlayHeight,
            useDevelopmentComparisonPlacement);

    public OverlayLocationSettings Capture(PhysicalRect overlayBounds) =>
        OverlayPlacementCalculator.Capture(overlayBounds, this.monitorService.GetMonitors());
}
