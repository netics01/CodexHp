using CodexHp.Core.Domain;

namespace CodexHp.App.Application;

public sealed class WindowsVisibilitySource
{
    private readonly IChatGptProcessDetector chatGptProcessDetector;
    private readonly IFullscreenDetector fullscreenDetector;
    private readonly IMonitorService monitorService;

    public WindowsVisibilitySource(
        IChatGptProcessDetector chatGptProcessDetector,
        IFullscreenDetector fullscreenDetector,
        IMonitorService monitorService)
    {
        this.chatGptProcessDetector = chatGptProcessDetector ?? throw new ArgumentNullException(nameof(chatGptProcessDetector));
        this.fullscreenDetector = fullscreenDetector ?? throw new ArgumentNullException(nameof(fullscreenDetector));
        this.monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
    }

    public VisibilityState Read(nint overlayWindowHandle)
    {
        var isChatGptRunning = this.chatGptProcessDetector.IsRunning();
        var overlayMonitor = this.monitorService.GetMonitorForWindow(overlayWindowHandle);
        var isFullscreen = overlayMonitor is not null
            && this.fullscreenDetector.IsFullscreenOnMonitor(overlayWindowHandle, overlayMonitor);
        return new VisibilityState(isChatGptRunning, isFullscreen);
    }
}
