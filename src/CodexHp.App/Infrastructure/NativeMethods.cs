using System.Runtime.InteropServices;
using System.Text;

namespace CodexHp.App.Infrastructure;

internal static class NativeMethods
{
    internal const uint MonitorInfoPrimary = 0x00000001;
    internal const uint MonitorDefaultToNearest = 0x00000002;
    internal const uint ProcessQueryLimitedInformation = 0x00001000;
    internal const int ErrorInsufficientBuffer = 122;
    internal const int EffectiveDpi = 0;
    internal const int SmCxVirtualScreen = 78;
    internal const uint DwmwaExtendedFrameBounds = 9;
    internal const uint DwmwaCloaked = 14;
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const int GwlHwndParent = -8;
    internal const uint WsPopup = 0x80000000;
    internal const uint WsChild = 0x40000000;
    internal const uint WsCaption = 0x00C00000;
    internal const uint WsThickFrame = 0x00040000;
    internal const uint WsSysMenu = 0x00080000;
    internal const uint WsMinimizeBox = 0x00020000;
    internal const uint WsMaximizeBox = 0x00010000;
    internal const uint WsExTopmost = 0x00000008;
    internal const uint WsExAppWindow = 0x00040000;
    internal const uint WsExLayered = 0x00080000;
    internal const uint WsExToolWindow = 0x00000080;
    internal const uint WsExNoActivate = 0x08000000;
    internal const uint TtsAlwaysTip = 0x00000001;
    internal const uint TtsNoPrefix = 0x00000002;
    internal const uint TtfIdIsHwnd = 0x0001;
    internal const uint TtfSubclass = 0x0010;
    internal const uint TtmActivate = 0x0401;
    internal const uint TtmSetDelayTime = 0x0403;
    internal const uint TtmGetToolCount = 0x040D;
    internal const uint TtmGetDelayTime = 0x0415;
    internal const uint TtmSetMaxTipWidth = 0x0418;
    internal const uint TtmGetMaxTipWidth = 0x0419;
    internal const uint TtmAddToolW = 0x0432;
    internal const uint TtmUpdateTipTextW = 0x0439;
    internal const uint TtmPop = 0x041C;
    internal const int TtDtInitial = 3;
    internal const uint IccWin95Classes = 0x000000FF;
    internal const uint DibRgbColors = 0;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint WmNcLeftButtonDown = 0x00A1;
    internal const uint WmLeftButtonDown = 0x0201;
    internal const uint WmLeftButtonDoubleClick = 0x0203;
    internal const int HitTestCaption = 2;
    internal const int TransparentBackgroundMode = 1;
    internal const int FontWeightSemiBold = 600;
    internal const uint DrawTextCenter = 0x00000001;
    internal const uint DrawTextVerticalCenter = 0x00000004;
    internal const uint DrawTextSingleLine = 0x00000020;
    internal static readonly nint HwndTop = nint.Zero;
    internal static readonly nint HwndTopmost = new(-1);
    internal static readonly nint DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    internal delegate bool MonitorEnumProc(
        nint monitorHandle,
        nint deviceContext,
        nint monitorRectangle,
        nint data);

    internal delegate bool EnumWindowsProc(nint windowHandle, nint data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InitCommonControlsData
    {
        internal uint Size;
        internal uint Classes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ToolInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint WindowHandle;
        internal nuint Id;
        internal NativeRect Rectangle;
        internal nint InstanceHandle;
        internal nint TextPointer;
        internal nint Data;
        internal nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int HorizontalPixelsPerMeter;
        internal int VerticalPixelsPerMeter;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        internal BitmapInfoHeader Header;
        internal uint Colors;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        internal int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceString;

        internal uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceKey;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint deviceContext,
        nint clipRectangle,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitorHandle, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(
        string deviceName,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW", SetLastError = true)]
    internal static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitCommonControlsEx(ref InitCommonControlsData controls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int left,
        int top,
        int width,
        int height,
        nint parentWindow,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern nint GetParent(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "SetParent", SetLastError = true)]
    private static extern nint SetParentNative(nint childWindow, nint newParent);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

    [DllImport("user32.dll", EntryPoint = "MapWindowPoints", SetLastError = true)]
    private static extern int MapWindowPointsNative(
        nint fromWindow,
        nint toWindow,
        ref NativePoint points,
        uint pointCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW", SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string messageName);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int left,
        int top,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessageW(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateSolidBrush(uint colorRef);

    [DllImport("user32.dll")]
    internal static extern int FillRect(nint deviceContext, ref NativeRect rectangle, nint brush);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint graphicObject);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowRgn(nint windowHandle, nint region, bool redraw);

    [DllImport("gdi32.dll")]
    internal static extern int SetBkMode(nint deviceContext, int backgroundMode);

    [DllImport("gdi32.dll")]
    internal static extern uint SetTextColor(nint deviceContext, uint colorRef);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint characterSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint deviceContext, nint graphicObject);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint windowHandle);

    [DllImport("gdi32.dll")]
    internal static extern uint GetPixel(nint deviceContext, int x, int y);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint windowHandle, nint deviceContext);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateDIBSection(
        nint deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint bits,
        nint section,
        uint offset);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int DrawTextW(
        nint deviceContext,
        string text,
        int textLength,
        ref NativeRect rectangle,
        uint format);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static extern int SetWindowLong32(nint windowHandle, int index, int newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint newValue);

    internal static nint GetWindowLongPointer(nint windowHandle, int index) =>
        nint.Size == 8 ? GetWindowLongPtr64(windowHandle, index) : GetWindowLong32(windowHandle, index);

    internal static bool TrySetWindowLongPointer(
        nint windowHandle,
        int index,
        nint newValue,
        out int lastError)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous;
        if (nint.Size == 8)
        {
            previous = SetWindowLongPtr64(windowHandle, index, newValue);
        }
        else
        {
            previous = new nint(SetWindowLong32(windowHandle, index, checked((int)newValue)));
        }

        lastError = Marshal.GetLastPInvokeError();
        return previous != nint.Zero || lastError == 0;
    }

    internal static bool TrySetParent(
        nint childWindow,
        nint newParent,
        out nint previousParent,
        out int lastError)
    {
        Marshal.SetLastPInvokeError(0);
        previousParent = SetParentNative(childWindow, newParent);
        lastError = Marshal.GetLastPInvokeError();
        return previousParent != nint.Zero || lastError == 0;
    }

    internal static bool TryMapWindowPoint(
        nint fromWindow,
        nint toWindow,
        ref NativePoint point,
        out int lastError)
    {
        Marshal.SetLastPInvokeError(0);
        var result = MapWindowPointsNative(fromWindow, toWindow, ref point, 1);
        lastError = Marshal.GetLastPInvokeError();
        return result != 0 || lastError == 0;
    }

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(
        nint monitorHandle,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static extern int DwmGetWindowAttributeInt(
        nint windowHandle,
        uint attribute,
        out int value,
        int valueSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static extern int DwmGetWindowAttributeRect(
        nint windowHandle,
        uint attribute,
        out NativeRect value,
        int valueSize);

    [DllImport("kernel32.dll")]
    internal static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetPackageFamilyName(
        nint processHandle,
        ref uint packageFamilyNameLength,
        StringBuilder? packageFamilyName);

}
