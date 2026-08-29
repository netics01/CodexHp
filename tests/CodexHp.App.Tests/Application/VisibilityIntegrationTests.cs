using CodexHp.App.Application;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class VisibilityIntegrationTests
{
    private static readonly MonitorGeometry Monitor = new(
        "DISPLAY1",
        new PhysicalRect(0, 0, 1920, 1080),
        new PhysicalRect(0, 0, 1920, 1040),
        1,
        1,
        true);

    [Fact]
    public void Always_visible_default_does_not_require_authentication_or_ChatGpt_process()
    {
        var source = new WindowsVisibilitySource(
            new FakeChatGptDetector(false),
            new FakeFullscreenDetector(false),
            new FakeMonitorService(Monitor));

        var state = Reduce(source.Read((nint)10), AppSettings.Default);

        Assert.True(state.IsVisible);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Conditional_visibility_tracks_official_ChatGpt_presence(bool isChatGptRunning, bool expectedVisible)
    {
        var source = new WindowsVisibilitySource(
            new FakeChatGptDetector(isChatGptRunning),
            new FakeFullscreenDetector(false),
            new FakeMonitorService(Monitor));
        var settings = AppSettings.Default with { ShowOnlyWhenChatGptRunning = true };

        var state = Reduce(source.Read((nint)10), settings);

        Assert.Equal(expectedVisible, state.IsVisible);
    }

    [Fact]
    public void Same_monitor_fullscreen_overrides_always_visible_setting()
    {
        var fullscreen = new FakeFullscreenDetector(true);
        var source = new WindowsVisibilitySource(
            new FakeChatGptDetector(true),
            fullscreen,
            new FakeMonitorService(Monitor));

        var state = Reduce(source.Read((nint)10), AppSettings.Default);

        Assert.False(state.IsVisible);
        Assert.Equal("DISPLAY1", fullscreen.LastMonitor?.Id);
    }

    [Fact]
    public void Missing_overlay_monitor_is_safe_and_does_not_report_fullscreen()
    {
        var fullscreen = new FakeFullscreenDetector(true);
        var source = new WindowsVisibilitySource(
            new FakeChatGptDetector(false),
            fullscreen,
            new FakeMonitorService(null));

        var visibility = source.Read((nint)10);

        Assert.False(visibility.IsFullscreenOnOverlayMonitor);
        Assert.Null(fullscreen.LastMonitor);
    }

    private static UsageOverlayState Reduce(VisibilityState visibility, AppSettings settings) =>
        UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Waiting,
            ServiceHealthState.Operational,
            string.Empty,
            visibility,
            settings,
            0);

    private sealed class FakeChatGptDetector(bool isRunning) : IChatGptProcessDetector
    {
        public bool IsRunning() => isRunning;
    }

    private sealed class FakeFullscreenDetector(bool isFullscreen) : IFullscreenDetector
    {
        public MonitorGeometry? LastMonitor { get; private set; }

        public bool IsFullscreenOnMonitor(nint overlayWindowHandle, MonitorGeometry overlayMonitor)
        {
            this.LastMonitor = overlayMonitor;
            return isFullscreen;
        }
    }

    private sealed class FakeMonitorService(MonitorGeometry? monitor) : IMonitorService
    {
        public IReadOnlyList<MonitorGeometry> GetMonitors() => monitor is null ? [] : [monitor];

        public MonitorGeometry? GetMonitorForWindow(nint windowHandle) => monitor;
    }
}
