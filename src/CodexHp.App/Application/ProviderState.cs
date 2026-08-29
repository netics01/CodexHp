using CodexHp.Core.Domain;

namespace CodexHp.App.Application;

public sealed record ProviderState(
    UsageProviderState Usage,
    TokenActivityProviderState TokenActivity,
    ServiceHealthState ServiceHealth,
    string ServiceStatusDescription,
    IReadOnlyList<string> ServiceAffectedComponents,
    VisibilityState Visibility)
{
    public static ProviderState Initial { get; } = new(
        UsageProviderState.Waiting,
        TokenActivityProviderState.Waiting,
        ServiceHealthState.Unknown,
        string.Empty,
        [],
        new VisibilityState(IsChatGptRunning: false, IsFullscreenOnOverlayMonitor: false));
}
