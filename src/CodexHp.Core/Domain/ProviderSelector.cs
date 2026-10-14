using CodexHp.Core.Settings;

namespace CodexHp.Core.Domain;

// Chooses the shown provider. In Automatic mode it follows the foreground desktop app,
// ignores brief Alt+Tab passes, and keeps the last provider while another app is active.
public sealed class ProviderSelector
{
    public static readonly TimeSpan DefaultDwell = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan DefaultEmphasis = TimeSpan.FromSeconds(2);

    private readonly long dwellMs;
    private readonly long emphasisMs;
    private UsageProvider? current;
    private UsageProvider? candidate;
    private long candidateSinceUnixMs;
    private long? switchedAtUnixMs;

    public ProviderSelector(TimeSpan? dwell = null, TimeSpan? emphasis = null)
    {
        this.dwellMs = (long)(dwell ?? DefaultDwell).TotalMilliseconds;
        this.emphasisMs = (long)(emphasis ?? DefaultEmphasis).TotalMilliseconds;
    }

    // When a different foreground app is waiting out the dwell time, the time it becomes the shown provider.
    public long? PendingSwitchUnixMs => this.candidate is null ? null : this.candidateSinceUnixMs + this.dwellMs;

    public ProviderSelection Update(
        ProviderMode mode,
        UsageProvider? foreground,
        bool isCodexAvailable,
        bool isClaudeAvailable,
        long nowUnixMs)
    {
        var target = mode switch
        {
            ProviderMode.Codex => UsageProvider.Codex,
            ProviderMode.Claude => UsageProvider.Claude,
            _ => this.SelectAutomatic(foreground, isCodexAvailable, isClaudeAvailable, nowUnixMs),
        };
        if (mode != ProviderMode.Automatic)
        {
            this.candidate = null;
        }

        if (this.current is null)
        {
            // The first selection is not a switch, so it is not emphasized.
            this.current = target;
        }
        else if (this.current != target)
        {
            this.current = target;
            this.switchedAtUnixMs = nowUnixMs;
        }

        var emphasized = this.switchedAtUnixMs is { } switchedAt
            && nowUnixMs >= switchedAt
            && nowUnixMs - switchedAt < this.emphasisMs;
        return new ProviderSelection(target, emphasized);
    }

    private UsageProvider SelectAutomatic(
        UsageProvider? foreground,
        bool isCodexAvailable,
        bool isClaudeAvailable,
        long nowUnixMs)
    {
        if (isCodexAvailable != isClaudeAvailable)
        {
            this.candidate = null;
            return isClaudeAvailable ? UsageProvider.Claude : UsageProvider.Codex;
        }

        if (this.current is null)
        {
            return foreground ?? UsageProvider.Codex;
        }

        if (foreground is not { } next || next == this.current)
        {
            this.candidate = null;
            return this.current.Value;
        }

        if (this.candidate != next)
        {
            this.candidate = next;
            this.candidateSinceUnixMs = nowUnixMs;
        }

        if (nowUnixMs - this.candidateSinceUnixMs < this.dwellMs)
        {
            return this.current.Value;
        }

        this.candidate = null;
        return next;
    }
}
