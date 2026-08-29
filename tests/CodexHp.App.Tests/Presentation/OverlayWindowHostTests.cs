using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using CodexHp.Core.Positioning;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class OverlayWindowHostTests
{
    [Fact]
    public void WPF_popup_shown_after_hosting_has_no_hidden_owner() =>
        StaTest.Run(() =>
    {
        var monitor = new WindowsMonitorService().GetMonitors().Single(item => item.IsPrimary);
        using var surface = new WpfOverlaySurface(32, 16, NoOpHook);
        var host = new OverlayWindowHost();
        var popupBounds = new PhysicalRect(
            monitor.Bounds.Left + 600,
            monitor.Bounds.Top + 600,
            32,
            16);

        _ = host.Apply(surface.WindowHandle, popupBounds, monitor.Id);
        surface.SetVisibility(true);

        Assert.Equal(OverlayHostMode.DesktopPopup, host.Mode);
        Assert.Equal(nint.Zero, NativeMethods.GetParent(surface.WindowHandle));
    });

    [Fact]
    public void Live_screen_window_round_trips_between_taskbar_child_and_popup() =>
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
                var host = new OverlayWindowHost();
                var overlayHeight = WindowsGuiTestGeometry.GetTaskbarCompatibleOverlayHeight(
                    taskbar.Value.TaskbarBounds);
                var requested = new PhysicalRect(
                    taskbar.Value.TaskbarBounds.Left + 2,
                    taskbar.Value.TaskbarBounds.Bottom - overlayHeight - 2,
                    288,
                    overlayHeight);
                var resolution = OverlayHostPlacementCalculator.Resolve(
                    requested,
                    monitor.Bounds,
                    taskbar.Value.TaskbarBounds);
                Assert.True(
                    resolution.Mode == OverlayHostMode.TaskbarChild,
                    $"Unexpected placement. Monitor={monitor.Bounds}, Taskbar={taskbar.Value.TaskbarBounds}, Requested={requested}");

                var hosted = host.Apply(window.WindowHandle, requested, monitor.Id);

                Assert.True(
                    host.Mode == OverlayHostMode.TaskbarChild,
                    $"Taskbar child transition failed: {host.LastTransitionFailure}");
                Assert.Equal(taskbar.Value.WindowHandle, host.TaskbarWindowHandle);
                Assert.Equal(taskbar.Value.WindowHandle, NativeMethods.GetParent(window.WindowHandle));
                Assert.Equal(requested, hosted);
                Assert.False(host.RequiresRecreation(window.WindowHandle));
                var childStyle = unchecked((uint)NativeMethods
                    .GetWindowLongPointer(window.WindowHandle, NativeMethods.GwlStyle)
                    .ToInt64());
                var childExtendedStyle = unchecked((uint)NativeMethods
                    .GetWindowLongPointer(window.WindowHandle, NativeMethods.GwlExStyle)
                    .ToInt64());
                Assert.NotEqual(0u, childStyle & NativeMethods.WsChild);
                Assert.Equal(0u, childStyle & NativeMethods.WsPopup);
                Assert.Equal(0u, childStyle & 0x00CF0000u);
                Assert.NotEqual(0u, childExtendedStyle & NativeMethods.WsExTopmost);
                Assert.Equal(0u, childExtendedStyle & 0x00040000u);
                Assert.NotEqual(0u, childExtendedStyle & NativeMethods.WsExLayered);

                var detached = host.DetachForDrag(window.WindowHandle);

                Assert.Equal(OverlayHostMode.DesktopPopup, host.Mode);
                Assert.Equal(nint.Zero, host.TaskbarWindowHandle);
                Assert.Equal(nint.Zero, NativeMethods.GetParent(window.WindowHandle));
                Assert.Equal(requested, detached);
                var popupStyle = unchecked((uint)NativeMethods
                    .GetWindowLongPointer(window.WindowHandle, NativeMethods.GwlStyle)
                    .ToInt64());
                var popupExtendedStyle = unchecked((uint)NativeMethods
                    .GetWindowLongPointer(window.WindowHandle, NativeMethods.GwlExStyle)
                    .ToInt64());
                Assert.NotEqual(0u, popupStyle & NativeMethods.WsPopup);
                Assert.Equal(0u, popupStyle & NativeMethods.WsChild);
                Assert.Equal(0u, popupStyle & 0x00CF0000u);
                Assert.NotEqual(0u, popupExtendedStyle & NativeMethods.WsExTopmost);
                Assert.Equal(0u, popupExtendedStyle & 0x00040000u);
                Assert.NotEqual(0u, popupExtendedStyle & NativeMethods.WsExLayered);
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
    public void Child_style_replaces_popup_and_topmost()
    {
        var style = OverlayWindowHost.BuildWindowStyle(0x80CF0000u, OverlayHostMode.TaskbarChild);
        var exStyle = OverlayWindowHost.BuildExtendedStyle(0x080C0088u, OverlayHostMode.TaskbarChild);

        Assert.Equal(0x40000000u, style & 0xC0000000u);
        Assert.Equal(0u, style & 0x00CF0000u);
        Assert.Equal(0u, exStyle & 0x00000008u);
        Assert.NotEqual(0u, exStyle & NativeMethods.WsExToolWindow);
        Assert.Equal(0u, exStyle & NativeMethods.WsExNoActivate);
        Assert.Equal(0u, exStyle & 0x00040000u);
        Assert.NotEqual(0u, exStyle & NativeMethods.WsExLayered);
    }

    [Fact]
    public void Popup_style_replaces_child_and_restores_topmost()
    {
        var style = OverlayWindowHost.BuildWindowStyle(0x40CF0000u, OverlayHostMode.DesktopPopup);
        var exStyle = OverlayWindowHost.BuildExtendedStyle(0x08040080u, OverlayHostMode.DesktopPopup);

        Assert.Equal(0x80000000u, style & 0xC0000000u);
        Assert.Equal(0u, style & 0x00CF0000u);
        Assert.Equal(0x00000008u, exStyle & 0x00000008u);
        Assert.Equal(0x08000080u, exStyle & 0x08000080u);
        Assert.Equal(0u, exStyle & 0x00040000u);
        Assert.NotEqual(0u, exStyle & NativeMethods.WsExLayered);
    }

    [Theory]
    [InlineData(false, 10, 10, 96, 96, true)]
    [InlineData(true, 10, 11, 96, 96, true)]
    [InlineData(true, 10, 10, 96, 120, true)]
    [InlineData(true, 10, 10, 96, 96, false)]
    [InlineData(true, 0, 0, 96, 96, true)]
    public void Hosted_window_health_detects_recreation_conditions(
        bool overlayAlive,
        long expectedParent,
        long actualParent,
        uint overlayDpi,
        uint taskbarDpi,
        bool expected)
    {
        var health = new TaskbarHostHealth(
            overlayAlive,
            new nint(expectedParent),
            new nint(actualParent),
            overlayDpi,
            taskbarDpi);

        Assert.Equal(expected, health.RequiresRecreation);
    }

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
}
