using CodexHp.Core.Positioning;

namespace CodexHp.App.Application;

public interface IFullscreenDetector
{
    bool IsFullscreenOnMonitor(nint overlayWindowHandle, MonitorGeometry overlayMonitor);
}
