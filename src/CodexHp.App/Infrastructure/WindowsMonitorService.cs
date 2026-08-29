using CodexHp.App.Application;
using CodexHp.Core.Positioning;

namespace CodexHp.App.Infrastructure;

public sealed class WindowsMonitorService : IMonitorService
{
    public IReadOnlyList<MonitorGeometry> GetMonitors()
    {
        var monitors = new List<MonitorGeometry>();
        NativeMethods.MonitorEnumProc callback = (monitorHandle, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfoEx
            {
                Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
            };

            if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
            {
                return true;
            }

            var scaleX = 1d;
            var scaleY = 1d;
            if (NativeMethods.GetDpiForMonitor(monitorHandle, NativeMethods.EffectiveDpi, out var dpiX, out var dpiY) >= 0)
            {
                scaleX = dpiX / 96d;
                scaleY = dpiY / 96d;
            }

            monitors.Add(new MonitorGeometry(
                info.DeviceName,
                ToPhysicalRect(info.Monitor),
                ToPhysicalRect(info.WorkArea),
                scaleX,
                scaleY,
                (info.Flags & NativeMethods.MonitorInfoPrimary) != 0,
                GetPersistentMonitorId(info.DeviceName)));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero))
        {
            throw new InvalidOperationException("Windows monitor enumeration failed.");
        }

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Windows did not report an attached monitor.");
        }

        return monitors;
    }

    public MonitorGeometry? GetMonitorForWindow(nint windowHandle)
    {
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

        var info = new NativeMethods.MonitorInfoEx
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
        {
            return null;
        }

        return this.GetMonitors().FirstOrDefault(monitor =>
            string.Equals(monitor.Id, info.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static PhysicalRect ToPhysicalRect(NativeMethods.NativeRect rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);

    private static string GetPersistentMonitorId(string deviceName)
    {
        var device = new NativeMethods.DisplayDevice
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DisplayDevice>(),
        };
        return NativeMethods.EnumDisplayDevices(deviceName, 0, ref device, 0)
            && !string.IsNullOrWhiteSpace(device.DeviceId)
                ? device.DeviceId
                : deviceName;
    }

}
