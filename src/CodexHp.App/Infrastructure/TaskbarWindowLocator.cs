using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CodexHp.Core.Positioning;

namespace CodexHp.App.Infrastructure;

public readonly record struct TaskbarWindowInfo(
    nint WindowHandle,
    string MonitorId,
    PhysicalRect TaskbarBounds,
    uint Dpi);

public sealed class TaskbarWindowLocator
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";

    public TaskbarWindowInfo? FindForMonitor(string monitorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

        return this.EnumerateCandidates()
            .Select(candidate => candidate.Info)
            .FirstOrDefault(candidate => string.Equals(
                candidate.MonitorId,
                monitorId,
                StringComparison.OrdinalIgnoreCase)) is { WindowHandle: not 0 } result
                ? result
                : null;
    }

    public TaskbarWindowInfo? FindForOverlayBounds(PhysicalRect overlayBounds)
    {
        if (overlayBounds.Width <= 0 || overlayBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlayBounds));
        }

        var candidates = this.EnumerateCandidates();
        if (candidates.Count == 0)
        {
            return null;
        }

        var center = overlayBounds.Center;
        return candidates
            .OrderBy(candidate => SquaredDistance(candidate.MonitorBounds, center))
            .Select(candidate => candidate.Info)
            .First();
    }

    private IReadOnlyList<TaskbarCandidate> EnumerateCandidates()
    {
        var candidates = new List<TaskbarCandidate>();
        NativeMethods.EnumWindowsProc callback = (windowHandle, _) =>
        {
            if (!IsTaskbarWindow(windowHandle))
            {
                return true;
            }

            var monitorHandle = NativeMethods.MonitorFromWindow(
                windowHandle,
                NativeMethods.MonitorDefaultToNearest);
            if (monitorHandle == nint.Zero)
            {
                return true;
            }

            var monitorInfo = new NativeMethods.MonitorInfoEx
            {
                Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
            };
            if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo)
                || !NativeMethods.GetWindowRect(windowHandle, out var taskbarRectangle))
            {
                return true;
            }

            candidates.Add(new TaskbarCandidate(
                new TaskbarWindowInfo(
                    windowHandle,
                    monitorInfo.DeviceName,
                    ToPhysicalRect(taskbarRectangle),
                    NativeMethods.GetDpiForWindow(windowHandle)),
                ToPhysicalRect(monitorInfo.Monitor)));
            return true;
        };

        if (!NativeMethods.EnumWindows(callback, nint.Zero))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows taskbar enumeration failed.");
        }

        return candidates;
    }

    private static bool IsTaskbarWindow(nint windowHandle)
    {
        var className = new StringBuilder(64);
        if (NativeMethods.GetClassName(windowHandle, className, className.Capacity) == 0)
        {
            return false;
        }

        return string.Equals(className.ToString(), PrimaryTaskbarClass, StringComparison.Ordinal)
            || string.Equals(className.ToString(), SecondaryTaskbarClass, StringComparison.Ordinal);
    }

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

    private static PhysicalRect ToPhysicalRect(NativeMethods.NativeRect rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);

    private readonly record struct TaskbarCandidate(
        TaskbarWindowInfo Info,
        PhysicalRect MonitorBounds);
}
