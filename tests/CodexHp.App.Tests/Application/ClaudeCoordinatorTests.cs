using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class ClaudeCoordinatorTests
{
    private const long Start = 10_000_000_000;

    [Fact]
    public async Task Shows_Claude_while_the_Claude_app_is_in_the_foreground_and_summarizes_Codex()
    {
        var harness = new Harness();
        harness.Visibility = new VisibilityState(true, false, true, UsageProvider.Claude);

        var state = await harness.PollAllAsync();

        Assert.Equal("Claude", state.ProviderLabel);
        Assert.False(state.IsProviderLabelEmphasized);
        Assert.Equal(9, state.ManaBar.RemainingPercent);
        Assert.Equal([5, 6], state.TokenBuckets);
        Assert.Equal(new OverlayTooltipRow("Codex", "5H 80% · Week 60%"), state.TooltipSections[^1].Rows.Single());
    }

    [Fact]
    public async Task Switches_to_Codex_after_the_dwell_time_with_a_brief_emphasis()
    {
        var harness = new Harness();
        harness.Visibility = new VisibilityState(true, false, true, UsageProvider.Claude);
        await harness.PollAllAsync();

        harness.Visibility = harness.Visibility with { ForegroundProvider = UsageProvider.Codex };
        var during = await harness.PollVisibilityAsync();
        harness.Clock.Now += 1_600;
        var switched = await harness.PollVisibilityAsync();
        harness.Clock.Now += 2_100;
        var settled = await harness.PollVisibilityAsync();

        Assert.Equal("Claude", during.ProviderLabel);
        Assert.Equal("Codex", switched.ProviderLabel);
        Assert.True(switched.IsProviderLabelEmphasized);
        Assert.Equal(80, switched.ManaBar.RemainingPercent);
        Assert.Equal([1, 2], switched.TokenBuckets);
        Assert.False(settled.IsProviderLabelEmphasized);
    }

    [Fact]
    public async Task Codex_without_a_sign_in_leaves_Claude_shown_and_omits_the_Codex_summary()
    {
        var harness = new Harness(codexSignedIn: false);
        harness.Visibility = new VisibilityState(false, false, true, UsageProvider.Codex);

        var state = await harness.PollAllAsync();

        Assert.Equal("Claude", state.ProviderLabel);
        Assert.DoesNotContain(state.TooltipSections, section => section.Rows.Any(row => row.Label == "Codex"));
    }

    [Fact]
    public async Task Missing_Claude_data_keeps_the_last_snapshot_as_failed()
    {
        var harness = new Harness();
        await harness.PollAllAsync();
        harness.ClaudeSnapshot = null;

        await harness.Coordinator.PollClaudeUsageOnceAsync(CancellationToken.None);

        Assert.Equal(ProviderAvailability.Failed, harness.Coordinator.CurrentClaudeState.Usage.Availability);
        Assert.NotNull(harness.Coordinator.CurrentClaudeState.Usage.LastSuccessful);
    }

    [Fact]
    public async Task A_pinned_provider_ignores_the_foreground_app()
    {
        var harness = new Harness(settings: AppSettings.Default with { ProviderMode = ProviderMode.Codex });
        harness.Visibility = new VisibilityState(true, false, true, UsageProvider.Claude);

        var state = await harness.PollAllAsync();

        Assert.Equal("Codex", state.ProviderLabel);
    }

    private sealed class Harness
    {
        private UsageOverlayState? latest;

        public Harness(bool codexSignedIn = true, AppSettings? settings = null)
        {
            this.Coordinator = new ApplicationCoordinator(
                new CredentialSource(codexSignedIn),
                new UsageClient(),
                new TokenSource([1, 2]),
                _ => Task.FromResult(OpenAiServiceStatusSnapshot.Unknown(Start) with { Health = ServiceHealthState.Operational }),
                () => this.Visibility,
                () => settings ?? AppSettings.Default,
                this.Clock,
                new NullLogger(),
                readGraphAppearance: () => new EffectiveAppearanceSettings(130, 32, 48, 1, 0, 2),
                claude: new ClaudeProviderSources(
                    new ClaudeUsage(this),
                    new TokenSource([5, 6]),
                    _ => Task.FromResult(OpenAiServiceStatusSnapshot.Unknown(Start) with { Health = ServiceHealthState.Operational })));
            this.Coordinator.UsageOverlayStateChanged += state => this.latest = state;
        }

        public ApplicationCoordinator Coordinator { get; }

        public ManualClock Clock { get; } = new() { Now = Start };

        public VisibilityState Visibility { get; set; } = new(true, false, true);

        public UsageSnapshot? ClaudeSnapshot { get; set; } = new(9, 87, Start + 3_600_000, 18_000, Start + 86_400_000, 604_800)
        {
            ObservedUnixMs = Start - 60_000,
            Source = "app cache",
        };

        public async Task<UsageOverlayState> PollAllAsync()
        {
            var none = CancellationToken.None;
            await this.Coordinator.PollUsageOnceAsync(none);
            await this.Coordinator.PollTokenActivityOnceAsync(none);
            await this.Coordinator.PollServiceStatusOnceAsync(none);
            await this.Coordinator.PollClaudeUsageOnceAsync(none);
            await this.Coordinator.PollClaudeTokenActivityOnceAsync(none);
            await this.Coordinator.PollClaudeServiceStatusOnceAsync(none);
            return await this.PollVisibilityAsync();
        }

        public async Task<UsageOverlayState> PollVisibilityAsync()
        {
            await this.Coordinator.PollVisibilityOnceAsync(CancellationToken.None);
            return this.latest!;
        }

        private sealed class ClaudeUsage(Harness harness) : IClaudeUsageSource
        {
            public UsageSnapshot? Read(long nowUnixMs) => harness.ClaudeSnapshot;
        }
    }

    private sealed class ManualClock : IClock
    {
        public long Now { get; set; }

        public long UnixTimeMilliseconds => this.Now;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class CredentialSource(bool signedIn) : ICodexCredentialSource
    {
        public CodexCredentials Load() => signedIn
            ? new CodexCredentials("access", null)
            : throw new CodexCredentialException(CodexCredentialFailure.MissingFile, "missing");
    }

    private sealed class UsageClient : IOpenAiUsageClient
    {
        public Task<UsageSnapshot> FetchAsync(
            CodexCredentials credentials,
            CancellationToken cancellationToken = default,
            bool includeResetCredits = false) =>
            Task.FromResult(new UsageSnapshot(80, 60, Start + 3_600_000, 18_000, Start + 86_400_000, 604_800));
    }

    private sealed class TokenSource(IReadOnlyList<int> buckets) : ICodexTokenActivitySource
    {
        public IReadOnlyList<int> ReadRecentTokenBuckets(long nowUnixMs, int bucketSeconds, int maxBuckets) => buckets;
    }

    private sealed class NullLogger : IDiagnosticLogger
    {
        public void Log(DiagnosticLevel level, string component, string message, Exception? exception = null)
        {
        }
    }
}
