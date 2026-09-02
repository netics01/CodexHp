using System.Windows.Interop;
using System.Windows.Threading;
using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation;

public sealed class UsageOverlayWindow
{
    private static readonly uint TaskbarCreatedMessage =
        NativeMethods.RegisterWindowMessage("TaskbarCreated");
    private readonly HwndSourceHook windowMessageHook;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer hostHealthTimer;
    private Func<nint, UsageOverlayLayout, bool>? surfacePresenter;
    private OverlayWindowHost windowHost;
    private WpfOverlaySurface? overlaySurface;
    private UsageOverlayState? usageOverlayState;
    private OverlayPresentationSettings presentationSettings =
        OverlayPresentationSettings.FromUnscaled(AppSettings.Default);
    private UsageOverlayLayout? layout;
    private PhysicalRect? lastHostedOverlayBounds;
    private string? lastMonitorId;
    private bool showRequested;
    private bool isClosed;
    private bool isRecreating;
    private bool hasHostedSurface;
    private bool recreationPending;

    public UsageOverlayWindow() : this(new OverlayWindowHost(), null)
    {
    }

    internal UsageOverlayWindow(OverlayWindowHost windowHost)
        : this(windowHost, null)
    {
    }

    internal UsageOverlayWindow(
        OverlayWindowHost windowHost,
        Func<nint, UsageOverlayLayout, bool>? surfacePresenter)
    {
        this.windowHost = windowHost ?? throw new ArgumentNullException(nameof(windowHost));
        this.surfacePresenter = surfacePresenter;
        this.windowMessageHook = this.DispatchWindowMessage;
        this.dispatcher = Dispatcher.CurrentDispatcher;
        this.hostHealthTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            this.OnHostHealthTimerTick,
            this.dispatcher);
        this.hostHealthTimer.Stop();
        try
        {
            this.WindowHandle = this.CreateWpfSurface();
            this.surfacePresenter ??= (_, layout) =>
                this.overlaySurface?.Present(layout) == true;
            this.hostHealthTimer.Start();
        }
        catch
        {
            this.overlaySurface?.Dispose();
            this.overlaySurface = null;
            throw;
        }
    }

    public event EventHandler? OpenSettingsRequested;

    public event Action<PhysicalRect>? OverlayPositionChanged;

    public event EventHandler? DisplayEnvironmentChangeRequested;

    public bool IsOverlayPositionChangeMode { get; private set; }

    public nint WindowHandle { get; private set; }

    internal bool IsStatusStripeTooltipEnabled =>
        this.overlaySurface?.IsStatusStripeTooltipEnabled == true;

    internal nint StatusStripeTooltipWindowHandle =>
        this.overlaySurface?.StatusStripeTooltipWindowHandle ?? nint.Zero;

    public static bool CanBeginDrag(bool isOverlayPositionChangeMode, uint message) =>
        isOverlayPositionChangeMode && message == NativeMethods.WmLeftButtonDown;

    public static bool IsTaskbarCreatedMessage(uint message, uint registeredMessage) =>
        registeredMessage != 0 && message == registeredMessage;

    internal static bool RequiresSurfaceRecreationAfterHostTransition(
        OverlayHostMode previousMode,
        OverlayHostMode currentMode) =>
        previousMode == OverlayHostMode.DesktopPopup
        && currentMode == OverlayHostMode.TaskbarChild;

    public void Apply(UsageOverlayState state, AppSettings nextSettings)
    {
        ArgumentNullException.ThrowIfNull(nextSettings);
        this.Apply(state, OverlayPresentationSettings.FromUnscaled(nextSettings));
    }

    public void Apply(UsageOverlayState state, OverlayPresentationSettings nextSettings)
    {
        ObjectDisposedException.ThrowIf(this.isClosed, this);
        this.usageOverlayState = state ?? throw new ArgumentNullException(nameof(state));
        this.presentationSettings = nextSettings ?? throw new ArgumentNullException(nameof(nextSettings));
        this.UpdateLayout();
        this.ApplyVisibility();
    }

    public void SetOverlayPositionChangeMode(bool isEnabled)
    {
        ObjectDisposedException.ThrowIf(this.isClosed, this);
        if (this.IsOverlayPositionChangeMode == isEnabled)
        {
            return;
        }

        this.IsOverlayPositionChangeMode = isEnabled;
        this.UpdateLayout();
    }

    public void SetPlacement(OverlayPlacement placement)
    {
        ObjectDisposedException.ThrowIf(this.isClosed, this);
        ArgumentNullException.ThrowIfNull(placement);
        var desiredOverlayBounds = new PhysicalRect(
            placement.PhysicalLeft,
            placement.PhysicalTop,
            placement.PhysicalWidth,
            placement.PhysicalHeight);
        _ = this.ApplyHostedPlacement(desiredOverlayBounds, placement.MonitorId);
    }

    internal PhysicalRect DetachForOverlayPositionDrag()
    {
        ObjectDisposedException.ThrowIf(this.isClosed, this);
        this.overlaySurface?.SetStatusStripeTooltipSuppressed(true);
        var detachedBounds = this.windowHost.DetachForDrag(this.WindowHandle);
        this.lastHostedOverlayBounds = detachedBounds;
        this.lastMonitorId = null;
        this.SubmitLayeredSurface();
        return detachedBounds;
    }

    internal PhysicalRect CompleteOverlayPositionDrag(PhysicalRect finalOverlayBounds)
    {
        ObjectDisposedException.ThrowIf(this.isClosed, this);
        var hostedOverlayBounds = this.ApplyHostedPlacement(finalOverlayBounds, monitorId: null);
        this.overlaySurface?.SetStatusStripeTooltipSuppressed(false);
        this.OverlayPositionChanged?.Invoke(hostedOverlayBounds);
        return hostedOverlayBounds;
    }

    public PhysicalRect? GetOverlayBounds()
    {
        if (this.WindowHandle == nint.Zero
            || !NativeMethods.GetWindowRect(this.WindowHandle, out var rectangle))
        {
            return null;
        }

        return new PhysicalRect(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(this.isClosed, this);
        this.showRequested = true;
        this.ApplyVisibility();
    }

    public void CloseForShutdown()
    {
        if (this.isClosed)
        {
            return;
        }

        this.isClosed = true;
        this.hostHealthTimer.Stop();
        this.WindowHandle = nint.Zero;
        this.overlaySurface?.Dispose();
        this.overlaySurface = null;
    }

    private nint CreateWpfSurface()
    {
        this.overlaySurface = new WpfOverlaySurface(
            this.presentationSettings.Appearance.OverlayWidth,
            this.presentationSettings.Appearance.OverlayHeight,
            this.windowMessageHook);
        this.overlaySurface.OpenSettingsRequested += this.OnSurfaceOpenSettingsRequested;
        return this.overlaySurface.WindowHandle;
    }

    private void OnSurfaceOpenSettingsRequested(object? sender, EventArgs eventArgs) =>
        this.OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    private nint DispatchWindowMessage(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        try
        {
            return this.HandleWindowMessage(
                windowHandle,
                unchecked((uint)message),
                wParam,
                lParam,
                ref handled);
        }
        catch
        {
            handled = false;
            return nint.Zero;
        }
    }

    private nint HandleWindowMessage(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (IsTaskbarCreatedMessage(message, TaskbarCreatedMessage))
        {
            handled = true;
            this.DisplayEnvironmentChangeRequested?.Invoke(this, EventArgs.Empty);
            this.ScheduleNativeWindowRecreation();
            return nint.Zero;
        }

        switch (message)
        {
            case NativeMethods.WmLeftButtonDown when CanBeginDrag(this.IsOverlayPositionChangeMode, message):
                handled = true;
                _ = this.DetachForOverlayPositionDrag();
                _ = NativeMethods.ReleaseCapture();
                _ = NativeMethods.SendMessageW(
                    windowHandle,
                    NativeMethods.WmNcLeftButtonDown,
                    new nint(NativeMethods.HitTestCaption),
                    nint.Zero);
                if (this.GetOverlayBounds() is { } bounds)
                {
                    _ = this.CompleteOverlayPositionDrag(bounds);
                }

                return nint.Zero;

            default:
                handled = false;
                return nint.Zero;
        }
    }

    private void UpdateLayout()
    {
        if (this.usageOverlayState is null || this.WindowHandle == nint.Zero)
        {
            return;
        }

        this.layout = UsageOverlayRenderer.CreateLayout(
            this.usageOverlayState,
            this.presentationSettings,
            this.IsOverlayPositionChangeMode);
        this.overlaySurface?.UpdateStatusStripeTooltip(this.usageOverlayState.StatusStripeTooltip);
        this.SubmitLayeredSurface();
    }

    private void SubmitLayeredSurface()
    {
        if (this.layout is not null
            && this.WindowHandle != nint.Zero
            && this.surfacePresenter is not null)
        {
            _ = this.surfacePresenter(this.WindowHandle, this.layout);
        }
    }

    private PhysicalRect ApplyHostedPlacement(PhysicalRect desiredOverlayBounds, string? monitorId)
    {
        var previousMode = this.windowHost.Mode;
        var hadHostedSurface = this.hasHostedSurface;
        var hostedOverlayBounds = this.windowHost.Apply(this.WindowHandle, desiredOverlayBounds, monitorId);
        this.hasHostedSurface = true;
        this.lastHostedOverlayBounds = hostedOverlayBounds;
        this.lastMonitorId = monitorId;
        this.SubmitLayeredSurface();

        if (hadHostedSurface
            && RequiresSurfaceRecreationAfterHostTransition(previousMode, this.windowHost.Mode))
        {
            this.ScheduleNativeWindowRecreation();
        }

        return hostedOverlayBounds;
    }

    private void ScheduleNativeWindowRecreation()
    {
        if (this.isClosed || this.recreationPending)
        {
            return;
        }

        this.recreationPending = true;
        _ = this.dispatcher.BeginInvoke(() =>
        {
            this.recreationPending = false;
            this.TryRecreateNativeWindow();
        });
    }

    private void ApplyVisibility()
    {
        if (!this.showRequested || this.usageOverlayState is null || this.WindowHandle == nint.Zero)
        {
            return;
        }

        this.overlaySurface?.SetVisibility(this.usageOverlayState.IsVisible);
        if (this.usageOverlayState.IsVisible)
        {
            this.SubmitLayeredSurface();
        }
    }

    private void OnHostHealthTimerTick(object? sender, EventArgs eventArgs)
    {
        if (this.isClosed || this.isRecreating)
        {
            return;
        }

        if (this.WindowHandle == nint.Zero
            || this.windowHost.RequiresRecreation(this.WindowHandle))
        {
            this.TryRecreateNativeWindow();
            return;
        }

        if (this.windowHost.Mode == OverlayHostMode.TaskbarChild
            && this.lastHostedOverlayBounds is { } expectedBounds
            && this.GetOverlayBounds() != expectedBounds)
        {
            try
            {
                _ = this.ApplyHostedPlacement(expectedBounds, this.lastMonitorId);
            }
            catch
            {
                // The health timer retries after the taskbar display transition settles.
            }
        }
    }

    private void TryRecreateNativeWindow()
    {
        if (this.isClosed || this.isRecreating)
        {
            return;
        }

        try
        {
            this.RecreateNativeWindow();
        }
        catch
        {
            // The health timer retries while the native host remains unavailable.
        }
    }

    private void RecreateNativeWindow()
    {
        this.isRecreating = true;
        try
        {
            var oldWindow = this.WindowHandle;
            var oldWindowAlive = oldWindow != nint.Zero && NativeMethods.IsWindow(oldWindow);
            var shouldBeVisible = oldWindowAlive
                ? NativeMethods.IsWindowVisible(oldWindow)
                : this.showRequested && this.usageOverlayState?.IsVisible == true;

            this.WindowHandle = nint.Zero;
            this.overlaySurface?.Dispose();
            this.overlaySurface = null;
            this.windowHost = new OverlayWindowHost();
            var replacement = this.CreateWpfSurface();
            this.WindowHandle = replacement;

            try
            {
                if (this.lastHostedOverlayBounds is { } desiredOverlayBounds)
                {
                    this.lastHostedOverlayBounds = this.windowHost.Apply(
                        replacement,
                        desiredOverlayBounds,
                        this.lastMonitorId);
                }

                this.UpdateLayout();
                this.overlaySurface?.SetVisibility(shouldBeVisible);
                if (shouldBeVisible)
                {
                    this.SubmitLayeredSurface();
                }
            }
            catch
            {
                this.overlaySurface?.Dispose();
                this.overlaySurface = null;
                this.WindowHandle = nint.Zero;
                throw;
            }
        }
        finally
        {
            this.isRecreating = false;
        }
    }
}
