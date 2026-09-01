using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

public sealed class ApplicationCoordinator
{
    private readonly object sync = new();
    private readonly ICodexCredentialSource credentialSource;
    private readonly IOpenAiUsageClient usageClient;
    private readonly ICodexTokenActivitySource tokenActivitySource;
    private readonly Func<CancellationToken, Task<OpenAiServiceStatusSnapshot>> readServiceStatus;
    private readonly Func<VisibilityState> readVisibility;
    private readonly Func<AppSettings> readSettings;
    private readonly Func<AppearanceSettings> readGraphAppearance;
    private readonly IClock clock;
    private readonly IDiagnosticLogger logger;
    private readonly PollSchedule schedule;
    private ProviderState providerState = ProviderState.Initial;
    private int hasRun;

    public ApplicationCoordinator(
        ICodexCredentialSource credentialSource,
        IOpenAiUsageClient usageClient,
        ICodexTokenActivitySource tokenActivitySource,
        Func<CancellationToken, Task<OpenAiServiceStatusSnapshot>> readServiceStatus,
        Func<VisibilityState> readVisibility,
        Func<AppSettings> readSettings,
        IClock clock,
        IDiagnosticLogger logger,
        Func<AppearanceSettings>? readGraphAppearance = null,
        PollSchedule? schedule = null)
    {
        this.credentialSource = credentialSource ?? throw new ArgumentNullException(nameof(credentialSource));
        this.usageClient = usageClient ?? throw new ArgumentNullException(nameof(usageClient));
        this.tokenActivitySource = tokenActivitySource ?? throw new ArgumentNullException(nameof(tokenActivitySource));
        this.readServiceStatus = readServiceStatus ?? throw new ArgumentNullException(nameof(readServiceStatus));
        this.readVisibility = readVisibility ?? throw new ArgumentNullException(nameof(readVisibility));
        this.readSettings = readSettings ?? throw new ArgumentNullException(nameof(readSettings));
        this.readGraphAppearance = readGraphAppearance ?? (() => this.readSettings().Appearance);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.schedule = schedule ?? PollSchedule.Default;
    }

    public event Action<UsageOverlayState>? UsageOverlayStateChanged;

    public ProviderState CurrentProviderState
    {
        get
        {
            lock (this.sync)
            {
                return this.providerState;
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref this.hasRun, 1) != 0)
        {
            throw new InvalidOperationException("The application coordinator can only be run once.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        this.PublishCurrentState(cancellationToken);

        await Task.WhenAll(
            this.RunUsageLoopAsync(cancellationToken),
            this.RunTokenActivityLoopAsync(cancellationToken),
            this.RunServiceStatusLoopAsync(cancellationToken),
            this.RunVisibilityLoopAsync(cancellationToken),
            this.RunRefreshGaugeLoopAsync(cancellationToken));
    }

    internal async Task PollUsageOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credentials = this.credentialSource.Load();
            var snapshot = await this.usageClient.FetchAsync(credentials, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            this.UpdateState(
                state => state with { Usage = UsageProviderState.Current(snapshot) },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.logger.Log(DiagnosticLevel.Warning, "Usage", "The usage provider failed.", exception);
            this.UpdateState(
                state => state with { Usage = UsageProviderState.Failed(state.Usage.LastSuccessful) },
                cancellationToken);
        }
    }

    internal Task PollTokenActivityOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var appearance = this.readGraphAppearance();
            var visibleBucketCount = TokenGraphViewport.CalculateVisibleBucketCount(appearance);
            var buckets = this.tokenActivitySource.ReadRecentTokenBuckets(
                this.clock.UnixTimeMilliseconds,
                TokenGraphViewport.BucketSeconds,
                Math.Max(1, visibleBucketCount));
            this.UpdateState(
                state => state with { TokenActivity = TokenActivityProviderState.Current(buckets) },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.logger.Log(DiagnosticLevel.Warning, "TokenActivity", "The token activity provider failed.", exception);
            this.UpdateState(
                state => state with { TokenActivity = TokenActivityProviderState.Failed },
                cancellationToken);
        }

        return Task.CompletedTask;
    }

    internal async Task PollServiceStatusOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await this.readServiceStatus(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            this.UpdateState(
                state => state with
                {
                    ServiceHealth = snapshot.Health,
                    ServiceStatusDescription = snapshot.Description,
                    ServiceAffectedComponents = snapshot.AffectedComponents ?? [],
                    ServiceAffectedGroups = snapshot.AffectedGroups ?? [],
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.logger.Log(DiagnosticLevel.Warning, "ServiceStatus", "The service status provider failed.", exception);
            this.UpdateState(
                state => state with
                {
                    ServiceHealth = ServiceHealthState.Unknown,
                    ServiceStatusDescription = string.Empty,
                    ServiceAffectedComponents = [],
                    ServiceAffectedGroups = [],
                },
                cancellationToken);
        }
    }

    internal Task PollVisibilityOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visibility = this.readVisibility();
            this.UpdateState(state => state with { Visibility = visibility }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.logger.Log(DiagnosticLevel.Warning, "Visibility", "The visibility provider failed.", exception);
            this.UpdateState(
                state => state with { Visibility = new VisibilityState(false, false) },
                cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task RunUsageLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await this.PollUsageOnceAsync(cancellationToken);
            await this.clock.DelayAsync(this.schedule.Usage, cancellationToken);
        }
    }

    private async Task RunTokenActivityLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await this.PollTokenActivityOnceAsync(cancellationToken);
            await this.clock.DelayAsync(this.schedule.TokenActivity, cancellationToken);
        }
    }

    private async Task RunServiceStatusLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await this.PollServiceStatusOnceAsync(cancellationToken);
            await this.clock.DelayAsync(this.schedule.ServiceStatusProbe, cancellationToken);
        }
    }

    private async Task RunVisibilityLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await this.PollVisibilityOnceAsync(cancellationToken);
            await this.clock.DelayAsync(this.schedule.Visibility, cancellationToken);
        }
    }

    private async Task RunRefreshGaugeLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await this.clock.DelayAsync(this.schedule.RefreshGauge, cancellationToken);
            this.PublishCurrentState(cancellationToken);
        }
    }

    private void UpdateState(
        Func<ProviderState, ProviderState> update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.sync)
        {
            this.providerState = update(this.providerState);
        }

        this.PublishCurrentState(cancellationToken);
    }

    private void PublishCurrentState(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UsageOverlayState usageOverlayState;
        try
        {
            lock (this.sync)
            {
                usageOverlayState = UsageOverlayStateReducer.Reduce(
                    this.providerState.Usage,
                    this.providerState.TokenActivity,
                    this.providerState.ServiceHealth,
                    this.providerState.ServiceStatusDescription,
                    this.providerState.Visibility,
                    this.readSettings(),
                    this.clock.UnixTimeMilliseconds,
                    this.providerState.ServiceAffectedComponents,
                    this.providerState.ServiceAffectedGroups);
            }
        }
        catch (Exception exception)
        {
            this.logger.Log(DiagnosticLevel.Error, "Coordinator", "The usage overlay state could not be reduced.", exception);
            return;
        }

        var handlers = this.UsageOverlayStateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<UsageOverlayState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(usageOverlayState);
            }
            catch (Exception exception)
            {
                this.logger.Log(DiagnosticLevel.Warning, "Coordinator", "A usage overlay state subscriber failed.", exception);
            }
        }
    }
}
