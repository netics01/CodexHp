using System.ComponentModel;
using System.Runtime.InteropServices;
using CodexHp.App.Infrastructure;

namespace CodexHp.App.Presentation;

internal static class AltTabWindowStyle
{
    internal static uint BuildExtendedStyle(uint current) =>
        (current | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;

    internal static uint BuildVisibleExtendedStyle(uint current) =>
        (current | NativeMethods.WsExAppWindow) & ~NativeMethods.WsExToolWindow;

    internal static void Apply(nint windowHandle) =>
        Apply(windowHandle, BuildExtendedStyle, "excluded from");

    internal static void ApplyVisible(nint windowHandle) =>
        Apply(windowHandle, BuildVisibleExtendedStyle, "included in");

    private static void Apply(
        nint windowHandle,
        Func<uint, uint> buildExtendedStyle,
        string action)
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            throw new ArgumentException("A live window is required.", nameof(windowHandle));
        }

        Marshal.SetLastPInvokeError(0);
        var current = NativeMethods.GetWindowLongPointer(
            windowHandle,
            NativeMethods.GwlExStyle);
        var lastError = Marshal.GetLastPInvokeError();
        if (current == nint.Zero && lastError != 0)
        {
            throw new Win32Exception(lastError, "The window style could not be read.");
        }

        var next = buildExtendedStyle(unchecked((uint)current.ToInt64()));
        if (next == unchecked((uint)current.ToInt64()))
        {
            return;
        }

        if (!NativeMethods.TrySetWindowLongPointer(
                windowHandle,
                NativeMethods.GwlExStyle,
                unchecked((nint)(nuint)next),
                out lastError))
        {
            throw new Win32Exception(lastError, $"The window could not be {action} Alt+Tab.");
        }

        if (!NativeMethods.SetWindowPos(
                windowHandle,
                NativeMethods.HwndTop,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoSize
                    | NativeMethods.SwpNoMove
                    | NativeMethods.SwpNoZOrder
                    | NativeMethods.SwpNoActivate
                    | NativeMethods.SwpFrameChanged))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "The Alt+Tab window style could not be refreshed.");
        }
    }
}
