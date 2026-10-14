using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;

namespace CodexHp.App.Application;

public sealed class WindowsVisibilitySource
{
    private readonly IChatGptProcessDetector chatGptProcessDetector;
    private readonly IFullscreenDetector fullscreenDetector;
    private readonly IMonitorService monitorService;
    private readonly IDesktopAppDetector? desktopAppDetector;

    public WindowsVisibilitySource(
        IChatGptProcessDetector chatGptProcessDetector,
        IFullscreenDetector fullscreenDetector,
        IMonitorService monitorService,
        IDesktopAppDetector? desktopAppDetector = null)
    {
        this.chatGptProcessDetector = chatGptProcessDetector ?? throw new ArgumentNullException(nameof(chatGptProcessDetector));
        this.fullscreenDetector = fullscreenDetector ?? throw new ArgumentNullException(nameof(fullscreenDetector));
        this.monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        this.desktopAppDetector = desktopAppDetector;
    }

    public VisibilityState Read(nint overlayWindowHandle)
    {
        var isChatGptRunning = this.chatGptProcessDetector.IsRunning();
        var overlayMonitor = this.monitorService.GetMonitorForWindow(overlayWindowHandle);
        var isFullscreen = overlayMonitor is not null
            && this.fullscreenDetector.IsFullscreenOnMonitor(overlayWindowHandle, overlayMonitor);
        var desktopApps = this.desktopAppDetector?.Read();
        return new VisibilityState(
            isChatGptRunning,
            isFullscreen,
            desktopApps?.IsClaudeRunning ?? false,
            desktopApps?.ForegroundProvider);
    }
}
