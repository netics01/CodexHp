using System.Runtime.InteropServices;
using CodexHp.App.Application;
using CodexHp.Core.Positioning;

namespace CodexHp.App.Infrastructure;

public sealed record ForegroundWindowSnapshot(
    nint Handle,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsShellWindow,
    PhysicalRect FrameBounds,
    string MonitorId);

public sealed class FullscreenDetector : IFullscreenDetector
{
    private const int EdgeTolerance = 2;
    private readonly Func<nint, ForegroundWindowSnapshot?> _snapshotProvider;

    public FullscreenDetector()
        : this(ReadForegroundWindow)
    {
    }

    public FullscreenDetector(Func<nint, ForegroundWindowSnapshot?> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
    }

    public bool IsFullscreenOnMonitor(nint overlayWindowHandle, MonitorGeometry overlayMonitor)
    {
        try
        {
            return ShouldHideFor(_snapshotProvider(overlayWindowHandle), overlayMonitor, overlayWindowHandle);
        }
        catch
        {
            return false;
        }
    }

    public static bool ShouldHideFor(
        ForegroundWindowSnapshot? foreground,
        MonitorGeometry overlayMonitor,
        nint overlayWindowHandle)
    {
        if (foreground is null
            || foreground.Handle == nint.Zero
            || foreground.Handle == overlayWindowHandle
            || !foreground.IsVisible
            || foreground.IsMinimized
            || foreground.IsCloaked
            || foreground.IsShellWindow
            || !string.Equals(foreground.MonitorId, overlayMonitor.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var window = foreground.FrameBounds;
        var monitor = overlayMonitor.Bounds;
        return window.Left <= monitor.Left + EdgeTolerance
            && window.Top <= monitor.Top + EdgeTolerance
            && window.Right >= monitor.Right - EdgeTolerance
            && window.Bottom >= monitor.Bottom - EdgeTolerance;
    }

    private static ForegroundWindowSnapshot? ReadForegroundWindow(nint overlayWindowHandle)
    {
        var windowHandle = NativeMethods.GetForegroundWindow();
        if (windowHandle == nint.Zero)
        {
            return null;
        }

        var monitorHandle = NativeMethods.MonitorFromWindow(
            windowHandle,
            NativeMethods.MonitorDefaultToNearest);
        if (monitorHandle == nint.Zero)
        {
            return null;
        }

        var monitorInfo = new NativeMethods.MonitorInfoEx
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return null;
        }

        var cloakedResult = NativeMethods.DwmGetWindowAttributeInt(
            windowHandle,
            NativeMethods.DwmwaCloaked,
            out var cloaked,
            sizeof(int));
        var frameResult = NativeMethods.DwmGetWindowAttributeRect(
            windowHandle,
            NativeMethods.DwmwaExtendedFrameBounds,
            out var frameBounds,
            Marshal.SizeOf<NativeMethods.NativeRect>());
        if (frameResult < 0 && !NativeMethods.GetWindowRect(windowHandle, out frameBounds))
        {
            return null;
        }

        return new ForegroundWindowSnapshot(
            windowHandle,
            NativeMethods.IsWindowVisible(windowHandle),
            NativeMethods.IsIconic(windowHandle),
            cloakedResult >= 0 && cloaked != 0,
            windowHandle == NativeMethods.GetShellWindow(),
            ToPhysicalRect(frameBounds),
            monitorInfo.DeviceName);
    }

    private static PhysicalRect ToPhysicalRect(NativeMethods.NativeRect rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
}
