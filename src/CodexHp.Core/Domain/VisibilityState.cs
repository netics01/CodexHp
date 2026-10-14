namespace CodexHp.Core.Domain;

// ForegroundProvider is null when the foreground window belongs to neither desktop app.
public sealed record VisibilityState(
    bool IsChatGptRunning,
    bool IsFullscreenOnOverlayMonitor,
    bool IsClaudeRunning = false,
    UsageProvider? ForegroundProvider = null);
