using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

/// <summary>Keeps temporary placement separate from the user's saved placement intent.</summary>
internal sealed class OverlayPlacementRecovery(
    Func<AppSettings, OverlayDisplayResolution> resolve,
    Action<AppSettings, OverlayDisplayResolution> apply,
    Func<OverlayPlacement, string?> verifyTaskbarPlacement,
    IDiagnosticLogger logger)
{
    private string? lastState;

    public bool IsPending { get; private set; }

    public bool Update(AppSettings settings, bool allowFallback)
    {
        var resolution = resolve(settings);
        var wantsTaskbar = settings.Location.Target == OverlayPlacementTarget.Taskbar;
        var unavailable = wantsTaskbar && resolution.TaskbarWasUnavailable;
        var tooSmall = wantsTaskbar && resolution.EffectiveTarget != OverlayPlacementTarget.Taskbar;

        // A startup/explicit preview needs a visible fallback. A retry only probes
        // availability; it must not repeatedly move or recreate that fallback.
        if (allowFallback || (!unavailable && !tooSmall))
        {
            apply(settings, resolution);
        }

        var failure = !wantsTaskbar ? null
            : unavailable ? "TaskbarUnavailable"
            : tooSmall ? "TaskbarTooSmall"
            : verifyTaskbarPlacement(resolution.Placement);
        var state = failure ?? (wantsTaskbar ? "TaskbarReady" : "DesktopSelected");
        this.Record(state, failure is not null,
            $"Requested={settings.Location.Target}; resolved={resolution.EffectiveTarget}; " +
            $"bounds={resolution.Placement.Bounds}; scale={resolution.DisplayScaleY}.");
        return this.IsPending;
    }

    public bool Refresh(AppSettings settings, bool positionEditing)
    {
        // The nested Windows drag loop can dispatch timers. Do not move the
        // window while the user is editing its position, even between drags.
        if (positionEditing)
        {
            return true;
        }

        try
        {
            return this.Update(settings, allowFallback: false);
        }
        catch (Exception exception)
        {
            this.Record($"RefreshFailed:{exception.GetType().Name}:{exception.Message}",
                settings.Location.Target == OverlayPlacementTarget.Taskbar,
                "Could not refresh overlay placement; retaining the current window.");
            return this.IsPending;
        }
    }

    private void Record(string state, bool pending, string details)
    {
        var wasPending = this.IsPending;
        this.IsPending = pending;
        if (state == this.lastState)
        {
            return;
        }

        var transition = pending ? "Recovery pending"
            : wasPending ? (state == "DesktopSelected" ? "Recovery cancelled" : "Recovery completed")
            : "Placement initialized";
        logger.Log(pending ? DiagnosticLevel.Warning : DiagnosticLevel.Information,
            "Placement", $"{transition}: {state}. {details}");
        this.lastState = state;
    }
}
