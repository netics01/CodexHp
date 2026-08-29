using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using System.Text;
using System.Windows.Threading;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class UsageOverlayHostingIntegrationTests
{
    [Fact]
    public void WPF_surface_visibility_uses_the_WPF_window_lifecycle() =>
        StaTest.Run(() =>
    {
        using var surface = new WpfOverlaySurface(32, 16, NoOpHook);

        Assert.False(surface.ShowInTaskbar);
        Assert.False(NativeMethods.IsWindowVisible(surface.WindowHandle));

        surface.SetVisibility(true);

        Assert.True(NativeMethods.IsWindowVisible(surface.WindowHandle));

        surface.SetVisibility(false);

        Assert.False(NativeMethods.IsWindowVisible(surface.WindowHandle));
    });

    [Fact]
    public void WPF_surface_requests_settings_only_for_a_left_double_click() =>
        StaTest.Run(() =>
    {
        using var surface = new WpfOverlaySurface(32, 16, NoOpHook);
        var requestCount = 0;
        surface.OpenSettingsRequested += (_, _) => requestCount++;

        surface.ProcessLeftButtonDown(1);
        surface.ProcessLeftButtonDown(2);

        Assert.Equal(1, requestCount);
    });

    [Fact]
    public void WPF_surface_owns_a_non_activating_status_tooltip_only_while_an_issue_description_exists() =>
        StaTest.Run(() =>
    {
        var surface = new WpfOverlaySurface(32, 16, NoOpHook);
        try
        {
            Assert.False(surface.IsStatusStripeTooltipEnabled);
            surface.SetVisibility(true);

            surface.UpdateStatusStripeTooltip("OpenAI service issue: Partial System Degradation");

            var tooltipWindow = surface.StatusStripeTooltipWindowHandle;
            Assert.True(surface.IsStatusStripeTooltipEnabled);
            Assert.NotEqual(nint.Zero, tooltipWindow);
            Assert.True(NativeMethods.IsWindow(tooltipWindow));
            var extendedStyle = unchecked((uint)NativeMethods.GetWindowLongPointer(
                tooltipWindow,
                NativeMethods.GwlExStyle).ToInt64());
            Assert.NotEqual(0u, extendedStyle & NativeMethods.WsExToolWindow);
            Assert.NotEqual(0u, extendedStyle & NativeMethods.WsExNoActivate);
            Assert.Equal(0u, extendedStyle & NativeMethods.WsExAppWindow);
            Assert.Equal(
                200,
                NativeMethods.SendMessageW(
                    tooltipWindow,
                    NativeMethods.TtmGetDelayTime,
                    new nint(NativeMethods.TtDtInitial),
                    nint.Zero).ToInt32());
            var virtualScreenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
            Assert.True(virtualScreenWidth > 0);
            Assert.Equal(
                virtualScreenWidth,
                NativeMethods.SendMessageW(
                    tooltipWindow,
                    NativeMethods.TtmGetMaxTipWidth,
                    nint.Zero,
                    nint.Zero).ToInt32());
            Assert.Equal(
                1,
                NativeMethods.SendMessageW(
                    tooltipWindow,
                    NativeMethods.TtmGetToolCount,
                    nint.Zero,
                    nint.Zero).ToInt32());

            surface.UpdateStatusStripeTooltip(null);

            Assert.False(surface.IsStatusStripeTooltipEnabled);

            surface.Dispose();

            Assert.False(NativeMethods.IsWindow(tooltipWindow));
        }
        finally
        {
            surface.Dispose();
        }
    });

    [Fact]
    public void Screen_area_uses_a_WPF_composition_surface_for_taskbar_hosting() =>
        StaTest.Run(() =>
    {
        var window = new UsageOverlayWindow();
        try
        {
            var className = new StringBuilder(256);

            Assert.NotEqual(
                0,
                NativeMethods.GetClassName(window.WindowHandle, className, className.Capacity));
            Assert.StartsWith("HwndWrapper[", className.ToString());
        }
        finally
        {
            window.CloseForShutdown();
        }
    });

    [Theory]
    [InlineData(0xC123u, 0xC123u, true)]
    [InlineData(0x0201u, 0xC123u, false)]
    [InlineData(0u, 0u, false)]
    public void Recognizes_only_the_registered_taskbar_created_message(
        uint message,
        uint registeredMessage,
        bool expected)
    {
        Assert.Equal(
            expected,
            UsageOverlayWindow.IsTaskbarCreatedMessage(message, registeredMessage));
    }

    [Fact]
    public void SetPlacement_routes_a_taskbar_position_through_child_hosting() =>
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

            var window = new UsageOverlayWindow();
            try
            {
                var initialWindowHandle = window.WindowHandle;
                var placement = new OverlayPlacement(
                    monitor.Id,
                    taskbar.Value.TaskbarBounds.Left + 2,
                    taskbar.Value.TaskbarBounds.Bottom - 68 - 2,
                    288,
                    68,
                    2,
                    taskbar.Value.TaskbarBounds.Bottom - 68 - 2 - monitor.Bounds.Top);

                window.SetPlacement(placement);

                Assert.Equal(initialWindowHandle, window.WindowHandle);
                Assert.Equal(taskbar.Value.WindowHandle, NativeMethods.GetParent(window.WindowHandle));
                Assert.Equal(
                    new PhysicalRect(
                        placement.PhysicalLeft,
                        placement.PhysicalTop,
                        placement.PhysicalWidth,
                        placement.PhysicalHeight),
                    window.GetOverlayBounds());
            }
            finally
            {
                window.CloseForShutdown();
            }
        }
        finally
        {
            _ = NativeMethods.SetThreadDpiAwarenessContext(previousDpiContext);
        }
    });

    [Fact]
    public void WPF_pixels_survive_taskbar_popup_and_taskbar_round_trip() =>
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
            var childPlacement = new OverlayPlacement(
                monitor.Id,
                taskbar.Value.TaskbarBounds.Left + 2,
                taskbar.Value.TaskbarBounds.Bottom - 68 - 2,
                288,
                68,
                2,
                taskbar.Value.TaskbarBounds.Bottom - 68 - 2 - monitor.Bounds.Top);
            var popupPlacement = new OverlayPlacement(
                monitor.Id,
                monitor.Bounds.Left + 600,
                monitor.Bounds.Top + 600,
                288,
                68,
                600,
                600);
            var visibleState = new UsageOverlayState(
                true,
                new GaugeDisplayState(100, 0.5, false),
                new GaugeDisplayState(100, 0.5, false),
                [],
                null,
                null);
            var window = new UsageOverlayWindow();
            try
            {
                window.Apply(visibleState, AppSettings.Default);
                window.SetPlacement(childPlacement);
                window.Show();
                var firstWindowHandle = window.WindowHandle;
                AssertOverlayPixelEventually(
                    childPlacement.PhysicalLeft + 10,
                    childPlacement.PhysicalTop + 10,
                    0x00FF8E3Au);

                window.SetPlacement(popupPlacement);
                AssertOverlayPixelEventually(
                    popupPlacement.PhysicalLeft + 10,
                    popupPlacement.PhysicalTop + 10,
                    0x00FF8E3Au);

                window.SetPlacement(childPlacement);
                PumpDispatcher();

                Assert.NotEqual(firstWindowHandle, window.WindowHandle);
                Assert.Equal(taskbar.Value.WindowHandle, NativeMethods.GetParent(window.WindowHandle));
                AssertOverlayPixelEventually(
                    childPlacement.PhysicalLeft + 10,
                    childPlacement.PhysicalTop + 10,
                    0x00FF8E3Au);
            }
            finally
            {
                window.CloseForShutdown();
            }
        }
        finally
        {
            _ = NativeMethods.SetThreadDpiAwarenessContext(previousDpiContext);
        }
    });

    [Fact]
    public void Service_issue_tooltip_rebinds_to_the_recreated_surface_after_taskbar_rehost() =>
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
            var taskbarPlacement = new OverlayPlacement(
                monitor.Id,
                taskbar.Value.TaskbarBounds.Left + 2,
                taskbar.Value.TaskbarBounds.Bottom - 68 - 2,
                288,
                68,
                2,
                taskbar.Value.TaskbarBounds.Bottom - 68 - 2 - monitor.Bounds.Top);
            var popupPlacement = new OverlayPlacement(
                monitor.Id,
                monitor.Bounds.Left + 600,
                monitor.Bounds.Top + 600,
                288,
                68,
                600,
                600);
            var issueState = new UsageOverlayState(
                true,
                new GaugeDisplayState(100, 0.5, false),
                new GaugeDisplayState(100, 0.5, false),
                [],
                AppSettings.Default.Colors.ServiceIssue,
                "OpenAI service issue: Partial System Degradation");
            var window = new UsageOverlayWindow();
            try
            {
                window.Apply(issueState, AppSettings.Default);
                window.SetPlacement(taskbarPlacement);
                window.Show();
                var firstOverlayWindow = window.WindowHandle;
                var firstTooltipWindow = window.StatusStripeTooltipWindowHandle;
                Assert.True(window.IsStatusStripeTooltipEnabled);

                window.SetPlacement(popupPlacement);
                window.SetPlacement(taskbarPlacement);
                PumpDispatcher();

                Assert.NotEqual(firstOverlayWindow, window.WindowHandle);
                Assert.True(window.IsStatusStripeTooltipEnabled);
                Assert.NotEqual(nint.Zero, window.StatusStripeTooltipWindowHandle);
                Assert.True(NativeMethods.IsWindow(window.StatusStripeTooltipWindowHandle));
                Assert.False(NativeMethods.IsWindow(firstTooltipWindow));
            }
            finally
            {
                window.CloseForShutdown();
            }
        }
        finally
        {
            _ = NativeMethods.SetThreadDpiAwarenessContext(previousDpiContext);
        }
    });

    [Fact]
    public void Service_issue_tooltip_is_suppressed_during_overlay_position_drag() =>
        StaTest.Run(() =>
    {
        var previousDpiContext = NativeMethods.SetThreadDpiAwarenessContext(
            NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        Assert.NotEqual(nint.Zero, previousDpiContext);
        try
        {
            var monitor = new WindowsMonitorService().GetMonitors().Single(item => item.IsPrimary);
            var popupPlacement = new OverlayPlacement(
                monitor.Id,
                monitor.Bounds.Left + 600,
                monitor.Bounds.Top + 600,
                288,
                68,
                600,
                600);
            var issueState = new UsageOverlayState(
                true,
                new GaugeDisplayState(100, 0.5, false),
                new GaugeDisplayState(100, 0.5, false),
                [],
                AppSettings.Default.Colors.ServiceIssue,
                "OpenAI service issue: Partial System Degradation");
            var window = new UsageOverlayWindow();
            try
            {
                window.Apply(issueState, AppSettings.Default);
                window.SetPlacement(popupPlacement);
                window.Show();
                Assert.True(window.IsStatusStripeTooltipEnabled);

                var detachedBounds = window.DetachForOverlayPositionDrag();

                Assert.False(window.IsStatusStripeTooltipEnabled);

                window.CompleteOverlayPositionDrag(detachedBounds);

                Assert.True(window.IsStatusStripeTooltipEnabled);
            }
            finally
            {
                window.CloseForShutdown();
            }
        }
        finally
        {
            _ = NativeMethods.SetThreadDpiAwarenessContext(previousDpiContext);
        }
    });

    [Fact]
    public void Applying_layout_and_rehosting_submit_the_layered_surface_synchronously() =>
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

            var paintCount = 0;
            var window = new UsageOverlayWindow(
                new OverlayWindowHost(),
                (_, _) =>
                {
                    paintCount++;
                    return true;
                });
            try
            {
                window.Apply(
                    new UsageOverlayState(
                        true,
                        new GaugeDisplayState(75, 0.5, false),
                        new GaugeDisplayState(40, 0.25, false),
                        [10_000, 55_000, 100_000],
                        null,
                        null),
                    AppSettings.Default);

                Assert.Equal(1, paintCount);

                window.SetPlacement(new OverlayPlacement(
                    monitor.Id,
                    taskbar.Value.TaskbarBounds.Left + 2,
                    taskbar.Value.TaskbarBounds.Bottom - 68 - 2,
                    288,
                    68,
                    2,
                    taskbar.Value.TaskbarBounds.Bottom - 68 - 2 - monitor.Bounds.Top));

                Assert.Equal(2, paintCount);
            }
            finally
            {
                window.CloseForShutdown();
            }
        }
        finally
        {
            _ = NativeMethods.SetThreadDpiAwarenessContext(previousDpiContext);
        }
    });

    private static nint NoOpHook(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        handled = false;
        return nint.Zero;
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

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
