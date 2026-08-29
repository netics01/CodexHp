using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using System.Windows.Threading;
using Xunit;
using Xunit.Abstractions;

namespace CodexHp.App.Tests.Presentation;

[Collection(WindowsGuiAcceptanceCollection.Name)]
public sealed class UsageOverlayDragAcceptanceTests(ITestOutputHelper output)
{
    private const int OverlayWidth = 288;
    private const uint ManaBarColorRef = 0x00FF8E3A;

    [Theory]
    [InlineData("AT-DRAG-001", DragStart.Taskbar, DragDestination.Outside, OverlayHostMode.DesktopPopup, false)]
    [InlineData("AT-DRAG-002", DragStart.Outside, DragDestination.Taskbar, OverlayHostMode.TaskbarChild, true)]
    [InlineData("AT-DRAG-003", DragStart.Taskbar, DragDestination.Midpoint, OverlayHostMode.TaskbarChild, true)]
    [InlineData("AT-DRAG-004", DragStart.Outside, DragDestination.Midpoint, OverlayHostMode.TaskbarChild, true)]
    public void Runtime_drag_keeps_the_screen_HWND_visible_and_topmost_at_its_physical_position(
        string acceptanceId,
        DragStart start,
        DragDestination destination,
        OverlayHostMode expectedMode,
        bool expectReplacementHandle) =>
        StaTest.Run(() =>
    {
        var previousDpiContext = NativeMethods.SetThreadDpiAwarenessContext(
            NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        Assert.NotEqual(nint.Zero, previousDpiContext);
        try
        {
            var monitor = new WindowsMonitorService().GetMonitors().Single(item => item.IsPrimary);
            var taskbar = new TaskbarWindowLocator().FindForMonitor(monitor.Id);
            Assert.NotNull(taskbar);
            Assert.Equal(monitor.Bounds.Bottom, taskbar.Value.TaskbarBounds.Bottom);
            var overlayHeight = WindowsGuiTestGeometry.GetTaskbarCompatibleOverlayHeight(
                taskbar.Value.TaskbarBounds);

            var left = Math.Clamp(
                monitor.Bounds.Left + 600,
                monitor.Bounds.Left,
                monitor.Bounds.Right - OverlayWidth);
            var childBounds = new PhysicalRect(
                left,
                taskbar.Value.TaskbarBounds.Bottom - overlayHeight - 2,
                OverlayWidth,
                overlayHeight);
            var popupBounds = new PhysicalRect(
                left,
                taskbar.Value.TaskbarBounds.Top - overlayHeight - 80,
                OverlayWidth,
                overlayHeight);
            var nearestPopupTop = taskbar.Value.TaskbarBounds.Top - overlayHeight;
            var nearestChildTop = taskbar.Value.TaskbarBounds.Bottom - overlayHeight - 2;
            var midpointBounds = new PhysicalRect(
                left,
                nearestPopupTop + ((nearestChildTop - nearestPopupTop) / 2),
                OverlayWidth,
                overlayHeight);
            var midpointResolution = OverlayHostPlacementCalculator.Resolve(
                midpointBounds,
                monitor.Bounds,
                taskbar.Value.TaskbarBounds);
            Assert.Equal(OverlayHostMode.TaskbarChild, midpointResolution.Mode);
            Assert.Equal(
                taskbar.Value.TaskbarBounds.Top
                    + ((taskbar.Value.TaskbarBounds.Height - overlayHeight) / 2),
                midpointResolution.OverlayBounds.Top);
            var initialBounds = start == DragStart.Taskbar ? childBounds : popupBounds;
            var completedDragBounds = destination switch
            {
                DragDestination.Outside => popupBounds,
                DragDestination.Taskbar => childBounds,
                DragDestination.Midpoint => midpointBounds,
                _ => throw new ArgumentOutOfRangeException(nameof(destination)),
            };
            var expectedBounds = destination == DragDestination.Midpoint
                ? midpointResolution.OverlayBounds
                : completedDragBounds;

            RunScenario(
                acceptanceId,
                monitor,
                taskbar.Value,
                initialBounds,
                completedDragBounds,
                expectedMode,
                expectedBounds,
                expectReplacementHandle);
        }
        finally
        {
            _ = NativeMethods.SetThreadDpiAwarenessContext(previousDpiContext);
        }
    });

    private void RunScenario(
        string acceptanceId,
        MonitorGeometry monitor,
        TaskbarWindowInfo taskbar,
        PhysicalRect initialBounds,
        PhysicalRect completedDragBounds,
        OverlayHostMode expectedMode,
        PhysicalRect expectedBounds,
        bool expectReplacementHandle)
    {
        var window = new UsageOverlayWindow();
        try
        {
            var settings = AppSettings.Default with
            {
                Appearance = AppSettings.Default.Appearance with
                {
                    OverlayHeight = initialBounds.Height,
                },
            };
            window.Apply(VisibleState(), settings);
            window.SetPlacement(ToPlacement(monitor, initialBounds));
            window.Show();
            PumpDispatcher();

            var initialWindowHandle = window.WindowHandle;
            _ = window.DetachForOverlayPositionDrag();
            PumpDispatcher();
            output.WriteLine(
                "{0} detached: HWND={1}, Alive={2}, Parent={3}, Style=0x{4:X8}, ExStyle=0x{5:X8}, Bounds={6}",
                acceptanceId,
                window.WindowHandle,
                NativeMethods.IsWindow(window.WindowHandle),
                NativeMethods.GetParent(window.WindowHandle),
                ReadStyle(window.WindowHandle, NativeMethods.GwlStyle),
                ReadStyle(window.WindowHandle, NativeMethods.GwlExStyle),
                window.GetOverlayBounds());
            Assert.True(NativeMethods.IsWindow(window.WindowHandle));
            var actualBounds = window.CompleteOverlayPositionDrag(completedDragBounds);
            PumpDispatcher();
            var finalWindowHandle = window.WindowHandle;

            Assert.Equal(expectedBounds, actualBounds);
            Assert.Equal(expectedBounds, window.GetOverlayBounds());
            if (expectReplacementHandle)
            {
                Assert.NotEqual(initialWindowHandle, finalWindowHandle);
            }
            else
            {
                Assert.Equal(initialWindowHandle, finalWindowHandle);
            }

            AssertNativeHost(finalWindowHandle, taskbar, expectedMode);
            AssertTopWindowAtProbeEventually(finalWindowHandle, expectedBounds.Left + 10, expectedBounds.Top + 10);
            AssertOverlayPixelEventually(expectedBounds.Left + 10, expectedBounds.Top + 10, ManaBarColorRef);

            output.WriteLine(
                "{0}: InitialHWND={1}, FinalHWND={2}, Parent={3}, Style=0x{4:X8}, ExStyle=0x{5:X8}, Bounds={6}",
                acceptanceId,
                initialWindowHandle,
                finalWindowHandle,
                NativeMethods.GetParent(finalWindowHandle),
                ReadStyle(finalWindowHandle, NativeMethods.GwlStyle),
                ReadStyle(finalWindowHandle, NativeMethods.GwlExStyle),
                expectedBounds);
        }
        finally
        {
            window.CloseForShutdown();
        }
    }

    private static void AssertNativeHost(
        nint windowHandle,
        TaskbarWindowInfo taskbar,
        OverlayHostMode expectedMode)
    {
        var style = ReadStyle(windowHandle, NativeMethods.GwlStyle);
        var extendedStyle = ReadStyle(windowHandle, NativeMethods.GwlExStyle);
        Assert.NotEqual(0u, extendedStyle & NativeMethods.WsExToolWindow);
        Assert.Equal(0u, extendedStyle & NativeMethods.WsExAppWindow);
        if (expectedMode == OverlayHostMode.TaskbarChild)
        {
            Assert.Equal(taskbar.WindowHandle, NativeMethods.GetParent(windowHandle));
            Assert.NotEqual(0u, style & NativeMethods.WsChild);
            Assert.Equal(0u, style & NativeMethods.WsPopup);
            Assert.NotEqual(0u, extendedStyle & NativeMethods.WsExTopmost);
            return;
        }

        Assert.Equal(nint.Zero, NativeMethods.GetParent(windowHandle));
        Assert.NotEqual(0u, style & NativeMethods.WsPopup);
        Assert.Equal(0u, style & NativeMethods.WsChild);
        Assert.NotEqual(0u, extendedStyle & NativeMethods.WsExTopmost);
    }

    private static void AssertTopWindowAtProbeEventually(nint expectedWindow, int x, int y)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        var actualWindow = nint.Zero;
        do
        {
            PumpDispatcher();
            _ = NativeMethods.DwmFlush();
            actualWindow = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint { X = x, Y = y });
            if (actualWindow == expectedWindow)
            {
                return;
            }

            Thread.Sleep(25);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Equal(expectedWindow, actualWindow);
    }

    private static void AssertOverlayPixelEventually(int x, int y, uint expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        var actual = uint.MaxValue;
        do
        {
            PumpDispatcher();
            _ = NativeMethods.DwmFlush();
            var screenDeviceContext = NativeMethods.GetDC(nint.Zero);
            Assert.NotEqual(nint.Zero, screenDeviceContext);
            try
            {
                actual = NativeMethods.GetPixel(screenDeviceContext, x, y);
            }
            finally
            {
                _ = NativeMethods.ReleaseDC(nint.Zero, screenDeviceContext);
            }

            if (actual == expected)
            {
                return;
            }

            Thread.Sleep(25);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Equal(expected, actual);
    }

    private static uint ReadStyle(nint windowHandle, int index) =>
        unchecked((uint)NativeMethods.GetWindowLongPointer(windowHandle, index).ToInt64());

    private static OverlayPlacement ToPlacement(MonitorGeometry monitor, PhysicalRect bounds) =>
        new(
            monitor.Id,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            bounds.Left - monitor.Bounds.Left,
            bounds.Top - monitor.Bounds.Top);

    private static UsageOverlayState VisibleState() =>
        new(
            true,
            new GaugeDisplayState(100, 0.5, false),
            new GaugeDisplayState(100, 0.5, false),
            [],
            null,
            null);

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public enum DragStart
    {
        Taskbar,
        Outside,
    }

    public enum DragDestination
    {
        Outside,
        Taskbar,
        Midpoint,
    }
}
