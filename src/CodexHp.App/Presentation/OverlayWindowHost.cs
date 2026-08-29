using System.ComponentModel;
using System.Runtime.InteropServices;
using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using CodexHp.Core.Positioning;

namespace CodexHp.App.Presentation;

internal sealed class OverlayWindowHost
{
    private readonly TaskbarWindowLocator taskbarLocator;
    private readonly IMonitorService monitorService;

    public OverlayWindowHost(
        TaskbarWindowLocator? taskbarLocator = null,
        IMonitorService? monitorService = null)
    {
        this.taskbarLocator = taskbarLocator ?? new TaskbarWindowLocator();
        this.monitorService = monitorService ?? new WindowsMonitorService();
    }

    public OverlayHostMode Mode { get; private set; } = OverlayHostMode.DesktopPopup;

    public nint TaskbarWindowHandle { get; private set; }

    internal string? LastTransitionFailure { get; private set; }

    public PhysicalRect Apply(
        nint windowHandle,
        PhysicalRect desiredOverlayBounds,
        string? monitorId)
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            throw new ArgumentException("A live usage overlay window is required.", nameof(windowHandle));
        }

        var monitor = this.SelectMonitor(desiredOverlayBounds, monitorId);
        var taskbar = this.taskbarLocator.FindForMonitor(monitor.Id);
        var placement = OverlayHostPlacementCalculator.Resolve(
            desiredOverlayBounds,
            monitor.Bounds,
            taskbar?.TaskbarBounds);
        this.LastTransitionFailure = null;

        if (placement.Mode == OverlayHostMode.TaskbarChild
            && taskbar is { } taskbarInfo)
        {
            try
            {
                if (this.ApplyChild(
                        windowHandle,
                        placement.OverlayBounds,
                        taskbarInfo,
                        out var failure))
                {
                    return placement.OverlayBounds;
                }

                this.LastTransitionFailure = failure;
            }
            catch (Win32Exception exception)
            {
                this.LastTransitionFailure = $"ReadStyle:{exception.NativeErrorCode}";
            }
        }

        var childTransitionFailure = this.LastTransitionFailure;
        try
        {
            this.ApplyPopupOrThrow(windowHandle, placement.OverlayBounds);
        }
        catch (Win32Exception exception) when (childTransitionFailure is not null)
        {
            throw new Win32Exception(
                exception.NativeErrorCode,
                $"Taskbar child hosting failed ({childTransitionFailure}); {exception.Message}");
        }

        return placement.OverlayBounds;
    }

    public PhysicalRect DetachForDrag(nint windowHandle)
    {
        if (windowHandle == nint.Zero
            || !NativeMethods.GetWindowRect(windowHandle, out var rectangle))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The usage overlay window bounds are unavailable for dragging.");
        }

        var overlayBounds = new PhysicalRect(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
        this.ApplyPopupOrThrow(windowHandle, overlayBounds);
        return overlayBounds;
    }

    public bool RequiresRecreation(nint windowHandle)
    {
        if (this.Mode != OverlayHostMode.TaskbarChild)
        {
            return false;
        }

        var overlayAlive = windowHandle != nint.Zero && NativeMethods.IsWindow(windowHandle);
        var actualParent = overlayAlive ? NativeMethods.GetParent(windowHandle) : nint.Zero;
        var overlayDpi = overlayAlive ? NativeMethods.GetDpiForWindow(windowHandle) : 0;
        var taskbarDpi = this.TaskbarWindowHandle != nint.Zero
            && NativeMethods.IsWindow(this.TaskbarWindowHandle)
                ? NativeMethods.GetDpiForWindow(this.TaskbarWindowHandle)
                : 0;

        return new TaskbarHostHealth(
            overlayAlive,
            this.TaskbarWindowHandle,
            actualParent,
            overlayDpi,
            taskbarDpi).RequiresRecreation;
    }

    internal static uint BuildWindowStyle(uint current, OverlayHostMode mode)
    {
        var frameStyles = NativeMethods.WsCaption
            | NativeMethods.WsThickFrame
            | NativeMethods.WsSysMenu
            | NativeMethods.WsMinimizeBox
            | NativeMethods.WsMaximizeBox;
        var frameless = current & ~frameStyles;
        return mode == OverlayHostMode.TaskbarChild
            ? (frameless & ~NativeMethods.WsPopup) | NativeMethods.WsChild
            : (frameless & ~NativeMethods.WsChild) | NativeMethods.WsPopup;
    }

    internal static uint BuildExtendedStyle(uint current, OverlayHostMode mode)
    {
        return mode == OverlayHostMode.TaskbarChild
            ? (current
                & ~NativeMethods.WsExTopmost
                & ~NativeMethods.WsExNoActivate
                & ~NativeMethods.WsExAppWindow)
                | NativeMethods.WsExLayered
                | NativeMethods.WsExToolWindow
            : (current & ~NativeMethods.WsExAppWindow)
                | NativeMethods.WsExTopmost
                | NativeMethods.WsExToolWindow
                | NativeMethods.WsExLayered
                | NativeMethods.WsExNoActivate;
    }

    private bool ApplyChild(
        nint windowHandle,
        PhysicalRect overlayBounds,
        TaskbarWindowInfo taskbar,
        out string? failure)
    {
        failure = null;
        var lastError = 0;
        if (!NativeMethods.TrySetParent(
                windowHandle,
                taskbar.WindowHandle,
                out _,
                out lastError))
        {
            failure = $"SetParent:{lastError}";
            return false;
        }

        var style = BuildWindowStyle(
            ReadWindowStyle(windowHandle, NativeMethods.GwlStyle),
            OverlayHostMode.TaskbarChild);
        var extendedStyle = BuildExtendedStyle(
            ReadWindowStyle(windowHandle, NativeMethods.GwlExStyle),
            OverlayHostMode.TaskbarChild);
        if (!NativeMethods.TrySetWindowLongPointer(
                windowHandle,
                NativeMethods.GwlStyle,
                ToNativeStyle(style),
                out lastError))
        {
            failure = $"SetStyle:{lastError}";
            return false;
        }

        if (!NativeMethods.TrySetWindowLongPointer(
                windowHandle,
                NativeMethods.GwlExStyle,
                ToNativeStyle(extendedStyle),
                out lastError))
        {
            failure = $"SetExtendedStyle:{lastError}";
            return false;
        }

        var actualChildParent = NativeMethods.GetParent(windowHandle);
        if (actualChildParent != taskbar.WindowHandle)
        {
            failure = $"VerifyParent:{actualChildParent}";
            return false;
        }

        var childOrigin = new NativeMethods.NativePoint
        {
            X = overlayBounds.Left,
            Y = overlayBounds.Top,
        };
        if (!NativeMethods.TryMapWindowPoint(
                nint.Zero,
                taskbar.WindowHandle,
                ref childOrigin,
                out lastError))
        {
            failure = $"MapWindowPoint:{lastError}";
            return false;
        }

        _ = NativeMethods.SetWindowRgn(windowHandle, nint.Zero, true);
        if (!NativeMethods.SetWindowPos(
                windowHandle,
                NativeMethods.HwndTop,
                childOrigin.X,
                childOrigin.Y,
                overlayBounds.Width,
                overlayBounds.Height,
                NativeMethods.SwpFrameChanged | NativeMethods.SwpNoActivate))
        {
            lastError = Marshal.GetLastWin32Error();
            failure = $"SetWindowPos:{lastError}";
            return false;
        }

        var region = NativeMethods.CreateRectRgn(0, 0, overlayBounds.Width, overlayBounds.Height);
        if (region == nint.Zero)
        {
            failure = "CreateWindowRegion:0";
            return false;
        }

        if (NativeMethods.SetWindowRgn(windowHandle, region, true) == 0)
        {
            _ = NativeMethods.DeleteObject(region);
            failure = $"SetWindowRegion:{Marshal.GetLastWin32Error()}";
            return false;
        }

        this.Mode = OverlayHostMode.TaskbarChild;
        this.TaskbarWindowHandle = taskbar.WindowHandle;
        return true;
    }

    private void ApplyPopupOrThrow(nint windowHandle, PhysicalRect overlayBounds)
    {
        if (!this.ApplyPopup(windowHandle, overlayBounds, out var failure, out var lastError))
        {
            this.LastTransitionFailure = failure;
            throw new Win32Exception(
                lastError,
                $"The usage overlay window could not switch to popup hosting ({failure}).");
        }
    }

    private bool ApplyPopup(
        nint windowHandle,
        PhysicalRect overlayBounds,
        out string? failure,
        out int lastError)
    {
        failure = null;
        lastError = 0;
        var transitionSucceeded = false;
        try
        {
            _ = NativeMethods.SetWindowRgn(windowHandle, nint.Zero, true);
            if (NativeMethods.GetParent(windowHandle) != nint.Zero
                && !NativeMethods.TrySetParent(
                    windowHandle,
                    nint.Zero,
                    out _,
                    out lastError))
            {
                failure = $"SetParent:{lastError}";
                return false;
            }

            var style = BuildWindowStyle(
                ReadWindowStyle(windowHandle, NativeMethods.GwlStyle),
                OverlayHostMode.DesktopPopup);
            var extendedStyle = BuildExtendedStyle(
                ReadWindowStyle(windowHandle, NativeMethods.GwlExStyle),
                OverlayHostMode.DesktopPopup) & ~NativeMethods.WsExTopmost;
            if (!NativeMethods.TrySetWindowLongPointer(
                    windowHandle,
                    NativeMethods.GwlStyle,
                    ToNativeStyle(style),
                    out lastError))
            {
                failure = $"SetStyle:{lastError}";
                return false;
            }

            if (!NativeMethods.TrySetWindowLongPointer(
                    windowHandle,
                    NativeMethods.GwlExStyle,
                    ToNativeStyle(extendedStyle),
                    out lastError))
            {
                failure = $"SetExtendedStyle:{lastError}";
                return false;
            }

            if (NativeMethods.GetParent(windowHandle) != nint.Zero
                && !NativeMethods.TrySetWindowLongPointer(
                    windowHandle,
                    NativeMethods.GwlHwndParent,
                    nint.Zero,
                    out lastError))
            {
                failure = $"ClearOwner:{lastError}";
                return false;
            }

            var actualPopupParent = NativeMethods.GetParent(windowHandle);
            if (actualPopupParent != nint.Zero)
            {
                failure = $"VerifyParent:{actualPopupParent}";
                return false;
            }

            if (!NativeMethods.SetWindowPos(
                    windowHandle,
                    NativeMethods.HwndTop,
                    overlayBounds.Left,
                    overlayBounds.Top,
                    overlayBounds.Width,
                    overlayBounds.Height,
                    NativeMethods.SwpFrameChanged | NativeMethods.SwpNoActivate))
            {
                if (lastError == 0)
                {
                    lastError = Marshal.GetLastWin32Error();
                }

                failure = $"SetWindowPos:{lastError}";
                return false;
            }

            this.Mode = OverlayHostMode.DesktopPopup;
            this.TaskbarWindowHandle = nint.Zero;
            transitionSucceeded = true;
            return true;
        }
        finally
        {
            if (transitionSucceeded
                && !NativeMethods.SetWindowPos(
                    windowHandle,
                    NativeMethods.HwndTopmost,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove
                        | NativeMethods.SwpNoSize
                        | NativeMethods.SwpNoActivate))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The popup usage overlay window could not become topmost.");
            }
        }
    }

    private MonitorGeometry SelectMonitor(PhysicalRect desiredOverlayBounds, string? monitorId)
    {
        var monitors = this.monitorService.GetMonitors();
        var identified = string.IsNullOrWhiteSpace(monitorId)
            ? null
            : monitors.FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                monitorId,
                StringComparison.OrdinalIgnoreCase));
        if (identified is not null)
        {
            return identified;
        }

        return monitors.FirstOrDefault(candidate => candidate.Bounds.Contains(desiredOverlayBounds.Center))
            ?? monitors
                .OrderBy(candidate => SquaredDistance(candidate.Bounds, desiredOverlayBounds.Center))
                .First();
    }

    private static uint ReadWindowStyle(nint windowHandle, int index)
    {
        Marshal.SetLastPInvokeError(0);
        var value = NativeMethods.GetWindowLongPointer(windowHandle, index);
        var lastError = Marshal.GetLastPInvokeError();
        if (value == nint.Zero && lastError != 0)
        {
            throw new Win32Exception(lastError, "The usage overlay window style could not be read.");
        }

        return unchecked((uint)value.ToInt64());
    }

    private static nint ToNativeStyle(uint style) =>
        unchecked((nint)(nuint)style);

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
}

internal readonly record struct TaskbarHostHealth(
    bool OverlayAlive,
    nint ExpectedParent,
    nint ActualParent,
    uint OverlayDpi,
    uint TaskbarDpi)
{
    public bool RequiresRecreation =>
        !this.OverlayAlive
        || this.ExpectedParent == nint.Zero
        || this.ActualParent != this.ExpectedParent
        || this.OverlayDpi == 0
        || this.TaskbarDpi == 0
        || this.OverlayDpi != this.TaskbarDpi;
}
