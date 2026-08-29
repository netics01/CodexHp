[CmdletBinding()]
param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\..\out\win-x64'),
    [int[]]$ExpectedOverlayBounds,
    [switch]$SkipPixelVerification,
    [switch]$AllowInstallerFiles,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
if (-not ('CodexHpWindowProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class CodexHpWindowProbe
{
    private const uint MonitorDefaultToNearest = 2;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    public static void EnablePhysicalDpiAwareness()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
    }

    public static IntPtr FindVisibleWindow(int processId, string expectedTitle)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((windowHandle, parameter) =>
        {
            if (Matches(windowHandle, processId, expectedTitle))
            {
                result = windowHandle;
                return false;
            }

            if (!IsTaskbarWindow(windowHandle))
            {
                return true;
            }

            EnumChildWindows(windowHandle, (childWindow, childParameter) =>
            {
                if (!Matches(childWindow, processId, expectedTitle))
                {
                    return true;
                }

                result = childWindow;
                return false;
            }, IntPtr.Zero);
            return result == IntPtr.Zero;
        }, IntPtr.Zero);
        return result;
    }

    public static IntPtr GetWindowParent(IntPtr windowHandle)
    {
        return GetParent(windowHandle);
    }

    public static IntPtr GetTaskbarParent(IntPtr windowHandle)
    {
        IntPtr parent = GetParent(windowHandle);
        return IsTaskbarWindow(parent) ? parent : IntPtr.Zero;
    }

    public static uint GetStyle(IntPtr windowHandle)
    {
        return unchecked((uint)GetWindowLongPtr(windowHandle, GwlStyle).ToInt64());
    }

    public static uint GetExtendedStyle(IntPtr windowHandle)
    {
        return unchecked((uint)GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64());
    }

    public static uint GetWindowDpi(IntPtr windowHandle)
    {
        return GetDpiForWindow(windowHandle);
    }

    public static string GetWindowClassName(IntPtr windowHandle)
    {
        var className = new StringBuilder(256);
        if (GetClassName(windowHandle, className, className.Capacity) == 0)
        {
            throw new InvalidOperationException("Window class name could not be read.");
        }

        return className.ToString();
    }

    public static int[] GetWindowBounds(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out NativeRect rectangle))
        {
            throw new InvalidOperationException("GetWindowRect failed.");
        }

        return new[]
        {
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top
        };
    }

    public static int[] GetMonitorBounds(IntPtr windowHandle)
    {
        IntPtr monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitorHandle == IntPtr.Zero || !GetMonitorInfo(monitorHandle, ref info))
        {
            throw new InvalidOperationException("Monitor bounds could not be read.");
        }

        return new[]
        {
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top
        };
    }

    public static uint GetScreenPixel(int x, int y)
    {
        IntPtr deviceContext = GetDC(IntPtr.Zero);
        if (deviceContext == IntPtr.Zero)
        {
            throw new InvalidOperationException("Screen device context could not be acquired.");
        }

        try
        {
            return GetPixel(deviceContext, x, y);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, deviceContext);
        }
    }

    private static bool Matches(IntPtr windowHandle, int processId, string expectedTitle)
    {
        GetWindowThreadProcessId(windowHandle, out uint candidateProcessId);
        if (candidateProcessId != processId || !IsWindowVisible(windowHandle))
        {
            return false;
        }

        int length = GetWindowTextLength(windowHandle);
        var title = new StringBuilder(length + 1);
        GetWindowText(windowHandle, title, title.Capacity);
        return string.Equals(title.ToString(), expectedTitle, StringComparison.Ordinal);
    }

    private static bool IsTaskbarWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var className = new StringBuilder(64);
        if (GetClassName(windowHandle, className, className.Capacity) == 0)
        {
            return false;
        }

        string value = className.ToString();
        return string.Equals(value, "Shell_TrayWnd", StringComparison.Ordinal)
            || string.Equals(value, "Shell_SecondaryTrayWnd", StringComparison.Ordinal);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(
        IntPtr parentWindow,
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);
}
'@
}

[CodexHpWindowProbe]::EnablePhysicalDpiAwareness()

$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$files = @(Get-ChildItem -LiteralPath $resolvedPublishDirectory -File)
$codexHpExecutables = @($files | Where-Object Name -eq 'CodexHp.exe')
if ($codexHpExecutables.Count -ne 1 -or (-not $AllowInstallerFiles -and $files.Count -ne 1)) {
    throw "Published directory must contain only CodexHp.exe. Found: $($files.Name -join ', ')"
}

$executablePath = $codexHpExecutables[0].FullName
$alreadyRunning = @(Get-Process -Name 'CodexHp' -ErrorAction SilentlyContinue)
if ($alreadyRunning.Count -gt 0) {
    throw 'Close the existing CodexHp process before published-app validation.'
}

$first = $null
$second = $null
try {
    $first = Start-Process -FilePath $executablePath -WindowStyle Hidden -PassThru
    $deadline = [DateTimeOffset]::Now.AddSeconds(10)
    $overlayWindowHandle = [IntPtr]::Zero
    do {
        Start-Sleep -Milliseconds 100
        $first.Refresh()
        if (-not $first.HasExited) {
            $overlayWindowHandle = [CodexHpWindowProbe]::FindVisibleWindow($first.Id, 'CodexHp')
        }
    } while (-not $first.HasExited -and $overlayWindowHandle -eq [IntPtr]::Zero -and [DateTimeOffset]::Now -lt $deadline)

    if ($first.HasExited) {
        throw "Published CodexHp exited during startup with code $($first.ExitCode)."
    }

    if ($overlayWindowHandle -eq [IntPtr]::Zero) {
        throw 'Published CodexHp did not create a visible usage-overlay window within 10 seconds.'
    }

    $actualBounds = [CodexHpWindowProbe]::GetWindowBounds($overlayWindowHandle)
    $monitorBounds = [CodexHpWindowProbe]::GetMonitorBounds($overlayWindowHandle)
    $overlayParent = [CodexHpWindowProbe]::GetWindowParent($overlayWindowHandle)
    $taskbarHandle = [CodexHpWindowProbe]::GetTaskbarParent($overlayWindowHandle)
    if ($taskbarHandle -eq [IntPtr]::Zero -or $overlayParent -ne $taskbarHandle) {
        throw 'Product-default usage overlay window is not hosted by its Windows taskbar.'
    }

    $overlayStyle = [CodexHpWindowProbe]::GetStyle($overlayWindowHandle)
    $overlayExtendedStyle = [CodexHpWindowProbe]::GetExtendedStyle($overlayWindowHandle)
    $overlayClassName = [CodexHpWindowProbe]::GetWindowClassName($overlayWindowHandle)
    if (-not $overlayClassName.StartsWith('HwndWrapper[', [StringComparison]::Ordinal)) {
        throw "Overlay surface is not WPF-composited. Class: $overlayClassName"
    }
    if (($overlayStyle -band 0x40000000) -eq 0) {
        throw 'Product-default usage overlay window is missing WS_CHILD.'
    }
    if (($overlayStyle -band 0x80000000) -ne 0) {
        throw 'Product-default usage overlay window still has WS_POPUP.'
    }
    if (($overlayStyle -band 0x00CF0000) -ne 0) {
        throw 'Taskbar-hosted usage overlay window still has caption or frame styles.'
    }
    if (($overlayExtendedStyle -band 0x00000008) -eq 0) {
        throw 'Taskbar-hosted WPF usage overlay window is missing WS_EX_TOPMOST.'
    }
    if (($overlayExtendedStyle -band 0x00040000) -ne 0) {
        throw 'Taskbar-hosted usage overlay window still has WS_EX_APPWINDOW.'
    }
    if (($overlayExtendedStyle -band 0x00000080) -eq 0) {
        throw 'Taskbar-hosted usage overlay window is missing WS_EX_TOOLWINDOW.'
    }
    if (($overlayExtendedStyle -band 0x00080000) -eq 0) {
        throw 'Taskbar-hosted usage overlay window is missing WS_EX_LAYERED.'
    }

    $taskbarBounds = [CodexHpWindowProbe]::GetWindowBounds($taskbarHandle)
    if ($PSBoundParameters.ContainsKey('ExpectedOverlayBounds')) {
        if ($ExpectedOverlayBounds.Count -ne 4) {
            throw 'ExpectedOverlayBounds must contain X, Y, width, and height.'
        }

        $expectedBounds = $ExpectedOverlayBounds
    }
    else {
        $defaultOverlayWidthDip = 144
        $defaultOverlayHeightDip = 34
        $edgeInsetDip = 2
        $dpi = [CodexHpWindowProbe]::GetWindowDpi($overlayWindowHandle)
        if ($dpi -eq 0) {
            throw 'The usage overlay DPI could not be read.'
        }
        $scale = [double]$dpi / 96.0
        $preferredWidth = [Math]::Max(1, [int][Math]::Round(
                $defaultOverlayWidthDip * $scale,
                [MidpointRounding]::AwayFromZero))
        $preferredHeight = [Math]::Max(1, [int][Math]::Round(
                $defaultOverlayHeightDip * $scale,
                [MidpointRounding]::AwayFromZero))
        $edgeInset = [Math]::Max(1, [int][Math]::Round(
                $edgeInsetDip * $scale,
                [MidpointRounding]::AwayFromZero))
        $horizontalTaskbar = $taskbarBounds[2] -ge $taskbarBounds[3]
        if ($horizontalTaskbar) {
            $expectedWidth = [Math]::Min(
                $preferredWidth,
                [Math]::Max(1, $taskbarBounds[2] - (2 * $edgeInset)))
            $expectedHeight = [Math]::Min(
                $preferredHeight,
                [Math]::Max(1, $taskbarBounds[3] - (2 * $edgeInset)))
            $maximumLeftOffset = [Math]::Max(0, $taskbarBounds[2] - $expectedWidth)
            $maximumTopOffset = [Math]::Max(0, $taskbarBounds[3] - $expectedHeight)
            $expectedLeft = $taskbarBounds[0] + [Math]::Min($edgeInset, $maximumLeftOffset)
            $expectedTop = $taskbarBounds[1] + [int][Math]::Floor($maximumTopOffset / 2.0)
        }
        else {
            $expectedWidth = [Math]::Min(
                $preferredWidth,
                [Math]::Max(1, $taskbarBounds[2] - (2 * $edgeInset)))
            $expectedHeight = [Math]::Min(
                $preferredHeight,
                [Math]::Max(1, $taskbarBounds[3] - (2 * $edgeInset)))
            $maximumLeftOffset = [Math]::Max(0, $taskbarBounds[2] - $expectedWidth)
            $maximumTopOffset = [Math]::Max(0, $taskbarBounds[3] - $expectedHeight)
            $expectedLeft = $taskbarBounds[0] + [int][Math]::Floor($maximumLeftOffset / 2.0)
            $expectedTop = $taskbarBounds[1] + [Math]::Min($edgeInset, $maximumTopOffset)
        }
        $expectedBounds = @(
            $expectedLeft,
            $expectedTop,
            $expectedWidth,
            $expectedHeight
        )
    }
    if ([string]::Join(',', $actualBounds) -ne [string]::Join(',', $expectedBounds)) {
        throw "Published CodexHp bounds were $([string]::Join(',', $actualBounds)); expected $([string]::Join(',', $expectedBounds))."
    }

    $overlayPixel = $null
    $taskbarPixel = $null
    if (-not $SkipPixelVerification) {
        $pixelDeadline = [DateTimeOffset]::Now.AddSeconds(2)
        do {
            $overlayPixel = [CodexHpWindowProbe]::GetScreenPixel(
                $actualBounds[0] + 10,
                $actualBounds[1] + 10)
            $taskbarPixel = [CodexHpWindowProbe]::GetScreenPixel(
                $actualBounds[0] + $actualBounds[2] + 20,
                $actualBounds[1] + 10)
            if ($overlayPixel -ne $taskbarPixel) {
                break
            }

            Start-Sleep -Milliseconds 50
        } while ([DateTimeOffset]::Now -lt $pixelDeadline)
        if ($overlayPixel -eq $taskbarPixel) {
            throw ('Usage overlay HWND exists, but its rendered pixels are indistinguishable from the taskbar ' +
                "at the validation point (0x$('{0:X8}' -f $overlayPixel)).")
        }
    }

    $second = Start-Process -FilePath $executablePath -WindowStyle Hidden -PassThru
    if (-not $second.WaitForExit(5000)) {
        throw 'A second CodexHp instance stayed running instead of exiting through the mutex guard.'
    }

    [pscustomobject]@{
        Executable = $executablePath
        ProcessId = $first.Id
        OverlayWindowHandle = $overlayWindowHandle.ToInt64()
        TaskbarWindowHandle = $taskbarHandle.ToInt64()
        OverlayStyle = ('0x{0:X8}' -f $overlayStyle)
        OverlayExtendedStyle = ('0x{0:X8}' -f $overlayExtendedStyle)
        OverlayClassName = $overlayClassName
        OverlayPixel = if ($null -eq $overlayPixel) { 'Skipped' } else { '0x{0:X8}' -f $overlayPixel }
        TaskbarPixel = if ($null -eq $taskbarPixel) { 'Skipped' } else { '0x{0:X8}' -f $taskbarPixel }
        OverlayBounds = [string]::Join(',', $actualBounds)
        SingleInstanceVerified = $true
        KeptRunning = [bool]$KeepRunning
    }
}
finally {
    if ($second -and -not $second.HasExited) {
        Stop-Process -Id $second.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not $KeepRunning -and $first -and -not $first.HasExited) {
        Stop-Process -Id $first.Id -Force -ErrorAction SilentlyContinue
    }
}
