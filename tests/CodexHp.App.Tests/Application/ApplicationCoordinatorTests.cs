using System.Collections.Concurrent;
using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class ApplicationCoordinatorTests
{
    private const long NowUnixMs = 1_000_000;

    [Fact]
    public void Default_schedule_matches_the_approved_polling_intervals()
    {
        var schedule = PollSchedule.Default;

        Assert.Equal(TimeSpan.FromSeconds(15), schedule.Usage);
        Assert.Equal(TimeSpan.FromSeconds(15), schedule.TokenActivity);
        Assert.Equal(TimeSpan.FromMinutes(1), schedule.ServiceStatusProbe);
        Assert.Equal(TimeSpan.FromSeconds(1), schedule.Visibility);
        Assert.Equal(TimeSpan.FromSeconds(1), schedule.RefreshGauge);
    }

    [Fact]
    public async Task Run_publishes_one_initial_waiting_state_before_provider_results()
    {
        var clock = new BlockingClock(NowUnixMs);
        var coordinator = CreateCoordinator(clock: clock);
        var published = new ConcurrentQueue<UsageOverlayState>();
        coordinator.UsageOverlayStateChanged += published.Enqueue;
        using var cancellation = new CancellationTokenSource();

        var runTask = coordinator.RunAsync(cancellation.Token);
        Assert.True(SpinWait.SpinUntil(() => clock.Delays.Count >= 5, TimeSpan.FromSeconds(2)));

        var first = Assert.IsType<UsageOverlayState>(published.FirstOrDefault());
        Assert.Null(first.ManaBar.RemainingPercent);
        Assert.Null(first.HpBar.RemainingPercent);
        Assert.Empty(first.TokenBuckets);
        Assert.Equal(AppSettings.Default.Colors.ServiceUnknown, first.StatusStripeColor);
        Assert.Equal(2, clock.Delays.Count(delay => delay == TimeSpan.FromSeconds(15)));
        Assert.Equal(2, clock.Delays.Count(delay => delay == TimeSpan.FromSeconds(1)));
        Assert.Contains(TimeSpan.FromMinutes(1), clock.Delays);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
    }

    [Fact]
    public async Task Provider_failure_does_not_prevent_other_provider_updates()
    {
        var clock = new BlockingClock(NowUnixMs);
        var coordinator = CreateCoordinator(
            clock: clock,
            fetchUsage: (_, _) => throw new HttpRequestException("offline"),
            readBuckets: (_, _, _) => [12, 34],
            readService: _ => Task.FromResult(new OpenAiServiceStatusSnapshot(
                ServiceHealthState.Operational,
                "none",
                "All Systems Operational",
                NowUnixMs,
                [])),
            readVisibility: () => new VisibilityState(true, false));
        using var cancellation = new CancellationTokenSource();

        var runTask = coordinator.RunAsync(cancellation.Token);
        Assert.True(SpinWait.SpinUntil(
            () => coordinator.CurrentProviderState is
            {
                Usage.Availability: ProviderAvailability.Failed,
                TokenActivity.Availability: ProviderAvailability.Current,
                ServiceHealth: ServiceHealthState.Operational,
                Visibility.IsChatGptRunning: true
            },
            TimeSpan.FromSeconds(2)));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
    }

    [Fact]
    public async Task Service_status_issue_publishes_the_server_description_as_overlay_tooltip()
    {
        var coordinator = CreateCoordinator(
            readService: _ => Task.FromResult(new OpenAiServiceStatusSnapshot(
                ServiceHealthState.Issue,
                "minor",
                "Partial System Degradation",
                NowUnixMs,
                ["ChatGPT", "Codex"])));
        var published = new List<UsageOverlayState>();
        coordinator.UsageOverlayStateChanged += published.Add;

        await coordinator.PollServiceStatusOnceAsync(CancellationToken.None);

        var state = Assert.Single(published);
        Assert.Equal(
            "OpenAI service issue: Partial System Degradation\r\nChatGPT, Codex",
            state.StatusStripeTooltip);
    }

    [Fact]
    public async Task Every_usage_poll_reloads_credentials_and_failure_keeps_last_success()
    {
        var credentialLoads = 0;
        var usageCalls = 0;
        var expected = SampleUsage();
        var coordinator = CreateCoordinator(
            loadCredentials: () =>
            {
                credentialLoads++;
                return new CodexCredentials("access", "account");
            },
            fetchUsage: (_, _) =>
            {
                usageCalls++;
                return usageCalls == 1
                    ? Task.FromResult(expected)
                    : throw new HttpRequestException("offline");
            });

        await coordinator.PollUsageOnceAsync(CancellationToken.None);
        await coordinator.PollUsageOnceAsync(CancellationToken.None);

        Assert.Equal(2, credentialLoads);
        Assert.Equal(2, usageCalls);
        Assert.Equal(ProviderAvailability.Failed, coordinator.CurrentProviderState.Usage.Availability);
        Assert.Equal(expected, coordinator.CurrentProviderState.Usage.LastSuccessful);
    }

    [Fact]
    public async Task Token_activity_poll_reads_the_complete_current_graph_viewport()
    {
        var requestedBucketSeconds = new List<int>();
        var requestedBucketCounts = new List<int>();
        var settings = AppSettings.Default;
        var graphAppearance = new AppearanceSettings(288, 68, 100, 2, 0, 4);
        var coordinator = CreateCoordinator(
            readBuckets: (_, bucketSeconds, maxBuckets) =>
            {
                requestedBucketSeconds.Add(bucketSeconds);
                requestedBucketCounts.Add(maxBuckets);
                return new int[maxBuckets];
            },
            readSettings: () => settings,
            readGraphAppearance: () => graphAppearance);

        await coordinator.PollTokenActivityOnceAsync(CancellationToken.None);
        settings = settings with
        {
            Appearance = settings.Appearance with
            {
                OverlayWidth = 400,
                GraphBarWidth = 5,
                GraphBarGap = 2,
            },
        };
        graphAppearance = new AppearanceSettings(400, 68, 100, 5, 2, 4);
        await coordinator.PollTokenActivityOnceAsync(CancellationToken.None);

        Assert.Equal([15, 15], requestedBucketSeconds);
        Assert.Equal([89, 41], requestedBucketCounts);
    }

    [Fact]
    public async Task Cancellation_during_provider_call_is_propagated_without_publishing_failure()
    {
        var coordinator = CreateCoordinator(
            fetchUsage: async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return SampleUsage();
            });
        var publishCount = 0;
        coordinator.UsageOverlayStateChanged += _ => publishCount++;
        using var cancellation = new CancellationTokenSource();

        var pollTask = coordinator.PollUsageOnceAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pollTask);
        Assert.Equal(0, publishCount);
        Assert.Equal(ProviderAvailability.Waiting, coordinator.CurrentProviderState.Usage.Availability);
    }

    [Fact]
    public async Task Completed_cancellation_emits_no_later_screen_states()
    {
        var clock = new BlockingClock(NowUnixMs);
        var coordinator = CreateCoordinator(clock: clock);
        var publishCount = 0;
        coordinator.UsageOverlayStateChanged += _ => Interlocked.Increment(ref publishCount);
        using var cancellation = new CancellationTokenSource();

        var runTask = coordinator.RunAsync(cancellation.Token);
        Assert.True(SpinWait.SpinUntil(() => clock.Delays.Count >= 5, TimeSpan.FromSeconds(2)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        var countAfterCompletion = publishCount;

        await Task.Delay(20);
        Assert.Equal(countAfterCompletion, publishCount);
    }

    private static ApplicationCoordinator CreateCoordinator(
        BlockingClock? clock = null,
        Func<CodexCredentials>? loadCredentials = null,
        Func<CodexCredentials, CancellationToken, Task<UsageSnapshot>>? fetchUsage = null,
        Func<long, int, int, IReadOnlyList<int>>? readBuckets = null,
        Func<CancellationToken, Task<OpenAiServiceStatusSnapshot>>? readService = null,
        Func<VisibilityState>? readVisibility = null,
        Func<AppSettings>? readSettings = null,
        Func<AppearanceSettings>? readGraphAppearance = null)
    {
        return new ApplicationCoordinator(
            new DelegateCredentialSource(loadCredentials ?? (() => new CodexCredentials("access", null))),
            new DelegateUsageClient(fetchUsage ?? ((_, _) => Task.FromResult(SampleUsage()))),
            new DelegateTokenSource(readBuckets ?? ((_, _, _) => [1, 2, 3])),
            readService ?? (_ => Task.FromResult(OpenAiServiceStatusSnapshot.Unknown(NowUnixMs))),
            readVisibility ?? (() => new VisibilityState(false, false)),
            readSettings ?? (() => AppSettings.Default),
            clock ?? new BlockingClock(NowUnixMs),
            new NullLogger(),
            readGraphAppearance: readGraphAppearance);
    }

    private static UsageSnapshot SampleUsage() => new(
        SessionRemainingPercent: 80,
        WeeklyRemainingPercent: 60,
        SessionResetUnixMs: NowUnixMs + 3_600_000,
        SessionWindowSeconds: 18_000,
        WeeklyResetUnixMs: NowUnixMs + 86_400_000,
        WeeklyWindowSeconds: 604_800);

    private sealed class DelegateCredentialSource(Func<CodexCredentials> load) : ICodexCredentialSource
    {
        public CodexCredentials Load() => load();
    }

    private sealed class DelegateUsageClient(
        Func<CodexCredentials, CancellationToken, Task<UsageSnapshot>> fetch) : IOpenAiUsageClient
    {
        public Task<UsageSnapshot> FetchAsync(
            CodexCredentials credentials,
            CancellationToken cancellationToken = default) => fetch(credentials, cancellationToken);
    }

    private sealed class DelegateTokenSource(
        Func<long, int, int, IReadOnlyList<int>> read) : ICodexTokenActivitySource
    {
        public IReadOnlyList<int> ReadRecentTokenBuckets(long nowUnixMs, int bucketSeconds, int maxBuckets) =>
            read(nowUnixMs, bucketSeconds, maxBuckets);
    }

    private sealed class BlockingClock(long nowUnixMs) : IClock
    {
        public ConcurrentBag<TimeSpan> Delays { get; } = [];

        public long UnixTimeMilliseconds => nowUnixMs;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class NullLogger : IDiagnosticLogger
    {
        public void Log(DiagnosticLevel level, string component, string message, Exception? exception = null)
        {
        }
    }
}
