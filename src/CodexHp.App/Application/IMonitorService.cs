using CodexHp.Core.Positioning;

namespace CodexHp.App.Application;

public interface IMonitorService
{
    IReadOnlyList<MonitorGeometry> GetMonitors();

    MonitorGeometry? GetMonitorForWindow(nint windowHandle);
}
