using System.Diagnostics;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using CodexHp.App.Application;

namespace CodexHp.App.Development;

internal enum SimulationTooltipMode { Hover, Pinned, Hidden }

internal sealed record SimulationPreset(
    string Id, string Name, string Description, bool BankedResets = false, UsageProvider Provider = UsageProvider.Codex)
{
    public override string ToString() => this.Name;
}

internal static class SimulationLaunchOptions
{
    internal static string Parse(IReadOnlyList<string> args)
    {
        if (args.Count is < 1 or > 2 || args[0] != "--simulate")
            throw new ArgumentException("Usage: CodexHp.exe --simulate [preset-id]");
        var id = args.Count == 2 ? args[1] : "normal";
        if (!SimulationSession.Presets.Any(p => p.Id == id))
            throw new ArgumentException($"Unknown simulation preset '{id}'. Available: {string.Join(", ", SimulationSession.Presets.Select(p => p.Id))}");
        return id;
    }
}

// Monotonic elapsed time is separate from simulated wall time. Freezing cannot race real polling.
internal sealed class SimulationClock
{
    private readonly Func<long> elapsedMilliseconds;
    private readonly long initialUnixMs;
    private long heldUnixMs;
    private long resumedAt;
    internal bool IsFrozen { get; private set; } = true;

    internal SimulationClock(long initialUnixMs, Func<long>? elapsedMilliseconds = null)
    {
        this.initialUnixMs = this.heldUnixMs = initialUnixMs;
        var stopwatch = Stopwatch.StartNew();
        this.elapsedMilliseconds = elapsedMilliseconds ?? (() => stopwatch.ElapsedMilliseconds);
    }

    internal long Now => this.heldUnixMs + (this.IsFrozen ? 0 : this.elapsedMilliseconds() - this.resumedAt);

    internal void SetFrozen(bool frozen)
    {
        this.heldUnixMs = this.Now;
        this.resumedAt = this.elapsedMilliseconds();
        this.IsFrozen = frozen;
    }

    internal void Advance(TimeSpan duration)
    {
        this.heldUnixMs = checked(this.Now + (long)duration.TotalMilliseconds);
        this.resumedAt = this.elapsedMilliseconds();
    }

    internal void Reset()
    {
        this.heldUnixMs = this.initialUnixMs;
        this.resumedAt = this.elapsedMilliseconds();
    }
}

internal sealed class SimulationSession
{
    internal static IReadOnlyList<SimulationPreset> Presets { get; } =
    [
        new("normal", "Normal / README", "5H 100%, Week 70%, repeatable token activity."),
        new("low-usage", "Usage running low", "5H 35%, Week 8%; both reset times are visible."),
        new("service-issue", "Service issue", "APIs and Codex incidents, grouped by product."),
        new("long-issue", "Long service issue", "Long incident details to test wrapping and overflow scrolling."),
        new("banked", "Banked resets", "Three credits; only the two nearest expirations appear.", true),
        new("banked-zero", "Banked resets — zero", "Confirmed zero credits, not a lookup failure.", true),
        new("banked-soon", "Banked resets — expiring soon", "Next credit expires in 30 minutes. Advance time to cross its expiry.", true),
        new("banked-missing", "Banked resets — missing details", "Count is known, expiration details are missing.", true),
        new("banked-unavailable", "Banked resets — unavailable", "Usage works, but credit inventory is unavailable.", true),
        new("loading", "Loading", "First usage request has not completed."),
        new("failed", "Usage unavailable", "No successful usage snapshot is available."),
        new("stale", "Last known usage", "A failed refresh retains the previous successful snapshot.", true),
        new("sign-in", "Sign-in required", "Codex credentials are missing."),
        new("reconnect", "Reconnect required", "Codex credentials cannot be used."),
        new("status-unknown", "Service status unknown", "Usage is available, service status is not."),
        new("update-available", "Update available", "A sample newer version appears in the tray and sample Settings. Links preview their target without opening a browser."),
        new("update-current", "No newer version", "Successful update check: no newer version; update links are hidden."),
        new("update-failed", "Update check failed", "Silent check failure. Any previously detected sample update stays available; otherwise links remain hidden."),
        new("claude", "Claude", "Claude app usage read 3 minutes ago; tooltip summarizes Codex.", Provider: UsageProvider.Claude),
        new("claude-stale", "Claude — older reading", "Last Claude app reading is 45 minutes old; values are dimmed.", Provider: UsageProvider.Claude),
        new("claude-expired", "Claude — no recent reading", "Last Claude app reading is 3 hours old; values are hidden.", Provider: UsageProvider.Claude),
        new("claude-reset-unknown", "Claude — reset time unknown", "Usage is known, reset times are not; reset gauges stay empty.", Provider: UsageProvider.Claude),
        new("claude-switch", "Switched to Claude", "Provider label is brighter right after a switch, then fades out over 2 seconds. Resume the clock to watch it fade.", Provider: UsageProvider.Claude),
    ];


    private long scenarioStart;
    internal SimulationClock Clock { get; }
    internal SimulationPreset Preset { get; private set; } = Presets[0];
    internal AvailableUpdate? AvailableUpdate { get; private set; }
    internal AppSettings Settings { get; set; } = AppSettings.Default with { StartWithWindows = false, ShowOnlyWhenChatGptRunning = false };

    internal SimulationSession(long initialUnixMs, Func<long>? elapsedMilliseconds = null)
    {
        this.Clock = new(initialUnixMs, elapsedMilliseconds);
        this.Select("normal");
    }

    internal void Select(string id)
    {
        this.Preset = Presets.Single(p => p.Id == id);
        if (id == "update-available")
        {
            var current = typeof(SimulationSession).Assembly.GetName().Version!;
            this.AvailableUpdate = Application.AvailableUpdate.FromTag($"v{current.Major}.{current.Minor}.{current.Build + 1}");
        }
        else if (id != "update-failed") this.AvailableUpdate = null;
        this.Clock.Reset();
        this.scenarioStart = this.Clock.Now;
        this.Settings = this.Settings with { UpperBarMode = this.Preset.BankedResets ? UpperBarMode.BankedResets : UpperBarMode.FiveHourUsage };
    }

    internal UsageOverlayState CreateState(int bucketCount)
    {
        var id = this.Preset.Id;
        var credits = id switch
        {
            "banked-zero" => new ResetCreditsSnapshot(0, []),
            "banked-missing" => new ResetCreditsSnapshot(3),
            "banked-unavailable" => null,
            "banked-soon" => new ResetCreditsSnapshot(2, [new("sample-a", this.scenarioStart + 1_800_000), new("sample-b", this.scenarioStart + 172_800_000)]),
            _ => new ResetCreditsSnapshot(3, [new("sample-c", this.scenarioStart + 432_000_000), new("sample-a", this.scenarioStart + 172_800_000), new("sample-b", this.scenarioStart + 259_200_000)]),
        };
        var snapshot = new UsageSnapshot(id == "low-usage" ? 35 : 100, id == "low-usage" ? 8 : 70,
            this.scenarioStart + 7_200_000, 18_000, this.scenarioStart + 270_600_000, 604_800) { ResetCredits = credits };
        if (this.Preset.Provider == UsageProvider.Claude)
            snapshot = this.CreateClaudeSnapshot(id);
        var usage = id switch
        {
            "loading" => UsageProviderState.Waiting,
            "failed" => UsageProviderState.Failed(),
            "stale" => UsageProviderState.Failed(snapshot),
            "sign-in" => UsageProviderState.Failed(failureReason: UsageFailureReason.SignInRequired),
            "reconnect" => UsageProviderState.Failed(failureReason: UsageFailureReason.ReconnectRequired),
            _ => UsageProviderState.Current(snapshot),
        };
        var issue = id is "service-issue" or "long-issue";
        IReadOnlyList<ServiceStatusComponentGroup> groups = id == "long-issue"
            ? Enumerable.Range(1, 24).Select(i => new ServiceStatusComponentGroup($"Example product {i}",
                ["Requests", "Conversations", "File uploads", "Search", "Connectors", "Image generation"])).ToArray()
            : [new("APIs", ["Responses", "Images"]), new("Codex", ["CLI", "Codex Web"])];
        var offset = (this.Clock.Now - this.scenarioStart) / (TokenGraphViewport.BucketSeconds * 1000L);
        int[] pattern = [0, 0, 240, 780, 1600, 1100, 450, 0, 90, 310, 920, 2100, 800, 0, 0, 130];
        var buckets = Enumerable.Range(0, Math.Max(1, bucketCount)).Select(i => pattern[(int)((i + offset) % pattern.Length)]).ToArray();
        return UsageOverlayStateReducer.Reduce(usage, TokenActivityProviderState.Current(buckets),
            issue ? ServiceHealthState.Issue : id == "status-unknown" ? ServiceHealthState.Unknown : ServiceHealthState.Operational,
            issue ? "Partial System Degradation" : "", new(true, false), this.Settings, this.Clock.Now,
            affectedServiceComponentGroups: groups, presentation: this.CreatePresentation());
    }

    private UsageSnapshot CreateClaudeSnapshot(string id)
    {
        var observedAgeMs = id switch
        {
            "claude-stale" => 2_700_000,
            "claude-expired" => 10_800_000,
            _ => 180_000,
        };
        var unknownReset = id == "claude-reset-unknown";
        return new UsageSnapshot(62, 81,
            unknownReset ? 0 : this.scenarioStart + 5_400_000, 18_000,
            unknownReset ? 0 : this.scenarioStart + 190_800_000, 604_800)
        {
            ObservedUnixMs = this.scenarioStart - observedAgeMs,
            Source = "app cache",
        };
    }

    // Codex presets keep the label the product shows when Claude is unavailable; Claude presets summarize a sample Codex.
    private ProviderPresentation CreatePresentation()
    {
        if (this.Preset.Provider == UsageProvider.Codex)
            return new(UsageProvider.Codex);
        var codex = UsageProviderState.Current(new UsageSnapshot(100, 70,
            this.scenarioStart + 7_200_000, 18_000, this.scenarioStart + 270_600_000, 604_800));
        var emphasis = this.Preset.Id == "claude-switch"
            ? ProviderSelector.EmphasisAt(
                this.Clock.Now - this.scenarioStart,
                (long)ProviderSelector.DefaultEmphasisHold.TotalMilliseconds,
                (long)ProviderSelector.DefaultEmphasisFade.TotalMilliseconds)
            : 0;
        return new(UsageProvider.Claude, emphasis,
            UsageOverlayStateReducer.CreateProviderSummary(UsageProvider.Codex, codex, ServiceHealthState.Operational, this.Clock.Now));
    }
}
