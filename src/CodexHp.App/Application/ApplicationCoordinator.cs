using System.Net.Http;
using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;

namespace CodexHp.App.Application;

public sealed record ClaudeProviderSources(
    IClaudeUsageSource Usage,
    ITokenActivitySource TokenActivity,
    Func<CancellationToken, Task<OpenAiServiceStatusSnapshot>> ReadServiceStatus);

public sealed class ApplicationCoordinator
{
    private readonly object sync = new();
    private readonly ICodexCredentialSource credentialSource;
    private readonly IOpenAiUsageClient usageClient;
    private readonly ICodexTokenActivitySource tokenActivitySource;
    private readonly Func<CancellationToken, Task<OpenAiServiceStatusSnapshot>> readServiceStatus;
    private readonly Func<VisibilityState> readVisibility;
    private readonly Func<AppSettings> readSettings;
    private readonly Func<EffectiveAppearanceSettings> readGraphAppearance;
    private readonly IClock clock;
    private readonly IDiagnosticLogger logger;
    private readonly PollSchedule schedule;
    private readonly ClaudeProviderSources? claude;
    private readonly ProviderSelector providerSelector = new();
    // Codex state also carries the shared visibility state.
    private ProviderState providerState = ProviderState.Initial;
    private ProviderState claudeState = ProviderState.Initial;
    private bool hasVisibility;
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
        Func<EffectiveAppearanceSettings>? readGraphAppearance = null,
        PollSchedule? schedule = null,
        ClaudeProviderSources? claude = null)
    {
        this.credentialSource = credentialSource ?? throw new ArgumentNullException(nameof(credentialSource));
        this.usageClient = usageClient ?? throw new ArgumentNullException(nameof(usageClient));
        this.tokenActivitySource = tokenActivitySource ?? throw new ArgumentNullException(nameof(tokenActivitySource));
        this.readServiceStatus = readServiceStatus ?? throw new ArgumentNullException(nameof(readServiceStatus));
        this.readVisibility = readVisibility ?? throw new ArgumentNullException(nameof(readVisibility));
        this.readSettings = readSettings ?? throw new ArgumentNullException(nameof(readSettings));
        this.readGraphAppearance = readGraphAppearance ?? (() => ToEffectiveAppearance(this.readSettings().Appearance));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.schedule = schedule ?? PollSchedule.Default;
        this.claude = claude;
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

    public ProviderState CurrentClaudeState
    {
        get
        {
            lock (this.sync)
            {
                return this.claudeState;
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

        var loops = new List<Task>
        {
            this.RunUsageLoopAsync(cancellationToken),
            this.RunTokenActivityLoopAsync(cancellationToken),
            this.RunServiceStatusLoopAsync(cancellationToken),
            this.RunVisibilityLoopAsync(cancellationToken),
            this.RunRefreshGaugeLoopAsync(cancellationToken),
        };
        if (this.claude is not null)
        {
            loops.Add(this.RunLoopAsync(this.PollClaudeUsageOnceAsync, this.schedule.Usage, cancellationToken));
            loops.Add(this.RunLoopAsync(this.PollClaudeTokenActivityOnceAsync, this.schedule.TokenActivity, cancellationToken));
            loops.Add(this.RunLoopAsync(this.PollClaudeServiceStatusOnceAsync, this.schedule.ServiceStatusProbe, cancellationToken));
        }

        await Task.WhenAll(loops);
    }

    internal Task PollClaudeUsageOnceAsync(CancellationToken cancellationToken)
    {
        if (this.claude is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = this.claude.Usage.Read(this.clock.UnixTimeMilliseconds);
            this.UpdateClaudeState(
                state => state with
                {
                    Usage = snapshot is null
                        ? UsageProviderState.Failed(state.Usage.LastSuccessful)
                        : UsageProviderState.Current(snapshot),
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.logger.Log(DiagnosticLevel.Warning, "ClaudeUsage", "The Claude usage provider failed.", exception);
            this.UpdateClaudeState(
                state => state with { Usage = UsageProviderState.Failed(state.Usage.LastSuccessful) },
                cancellationToken);
        }

        return Task.CompletedTask;
    }

    internal Task PollClaudeTokenActivityOnceAsync(CancellationToken cancellationToken)
    {
        if (this.claude is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visibleBucketCount = TokenGraphViewport.CalculateVisibleBucketCount(this.readGraphAppearance());
            var buckets = this.claude.TokenActivity.ReadRecentTokenBuckets(
                this.clock.UnixTimeMilliseconds,
                TokenGraphViewport.BucketSeconds,
                Math.Max(1, visibleBucketCount));
            this.UpdateClaudeState(
                state => state with { TokenActivity = TokenActivityProviderState.Current(buckets) },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.logger.Log(DiagnosticLevel.Warning, "ClaudeTokenActivity", "The Claude token activity provider failed.", exception);
            this.UpdateClaudeState(
                state => state with { TokenActivity = TokenActivityProviderState.Failed },
                cancellationToken);
        }

        return Task.CompletedTask;
    }

    internal async Task PollClaudeServiceStatusOnceAsync(CancellationToken cancellationToken)
    {
        if (this.claude is null)
        {
            return;
        }

        try
        {
            var snapshot = await this.claude.ReadServiceStatus(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            this.UpdateClaudeState(state => WithServiceStatus(state, snapshot), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.logger.Log(DiagnosticLevel.Warning, "ClaudeServiceStatus", "The Claude service status provider failed.", exception);
            this.UpdateClaudeState(
                state => WithServiceStatus(state, OpenAiServiceStatusSnapshot.Unknown(this.clock.UnixTimeMilliseconds)),
                cancellationToken);
        }
    }

    private static ProviderState WithServiceStatus(ProviderState state, OpenAiServiceStatusSnapshot snapshot) =>
        state with
        {
            ServiceHealth = snapshot.Health,
            ServiceStatusDescription = snapshot.Description,
            ServiceAffectedComponents = snapshot.AffectedComponents ?? [],
            ServiceAffectedGroups = snapshot.AffectedGroups ?? [],
            ServiceAffectedComponentGroups = snapshot.AffectedComponentGroups ?? [],
        };

    private async Task RunLoopAsync(
        Func<CancellationToken, Task> poll,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await poll(cancellationToken);
            await this.clock.DelayAsync(interval, cancellationToken);
        }
    }

    private void UpdateClaudeState(Func<ProviderState, ProviderState> update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.sync)
        {
            this.claudeState = update(this.claudeState);
        }

        this.PublishCurrentState(cancellationToken);
    }

    internal async Task PollUsageOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credentials = this.credentialSource.Load();
            var snapshot = await this.usageClient.FetchAsync(
                credentials, cancellationToken,
                includeResetCredits: this.readSettings().UpperBarMode == UpperBarMode.BankedResets);
            cancellationToken.ThrowIfCancellationRequested();
            this.UpdateState(
                state => state with { Usage = UsageProviderState.Current(snapshot) },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CodexCredentialException exception)
        {
            this.logger.Log(DiagnosticLevel.Warning, "Usage", "The usage provider failed.", exception);
            var failureReason = exception.Failure is
                CodexCredentialFailure.MissingFile or CodexCredentialFailure.MissingAccessToken
                    ? UsageFailureReason.SignInRequired
                    : UsageFailureReason.ReconnectRequired;
            this.UpdateState(
                state => state with
                {
                    Usage = UsageProviderState.Failed(state.Usage.LastSuccessful, failureReason),
                },
                cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            this.logger.Log(DiagnosticLevel.Warning, "Usage", "The usage provider rejected the Codex credentials.", exception);
            this.UpdateState(
                state => state with
                {
                    Usage = UsageProviderState.Failed(
                        state.Usage.LastSuccessful,
                        UsageFailureReason.ReconnectRequired),
                },
                cancellationToken);
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

    private static EffectiveAppearanceSettings ToEffectiveAppearance(AppearanceSettings appearance) =>
        new(
            appearance.OverlayWidth,
            appearance.OverlayHeight,
            appearance.GaugePaneWidth,
            appearance.GraphBarWidth,
            appearance.GraphBarGap,
            appearance.StatusStripeWidth);

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
                    ServiceAffectedComponentGroups = snapshot.AffectedComponentGroups ?? [],
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
                    ServiceAffectedComponentGroups = [],
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
            this.UpdateState(
                state =>
                {
                    this.hasVisibility = true;
                    return state with { Visibility = visibility };
                },
                cancellationToken);
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

    // Called under the state lock.
    private (ProviderState Shown, ProviderPresentation Presentation) SelectProvider(
        AppSettings settings,
        VisibilityState visibility,
        long nowUnixMs)
    {
        var codexUsage = this.providerState.Usage;
        var isCodexAvailable = visibility.IsChatGptRunning
            || codexUsage.LastSuccessful is not null
            || codexUsage is not { Availability: ProviderAvailability.Failed, FailureReason: UsageFailureReason.SignInRequired };
        var isClaudeAvailable = visibility.IsClaudeRunning || this.claudeState.Usage.LastSuccessful is not null;
        // Until the foreground app is first observed, show a provisional choice without
        // committing it, so startup does not count as a switch.
        var selection = this.hasVisibility
            ? this.providerSelector.Update(
                settings.ProviderMode,
                visibility.ForegroundProvider,
                isCodexAvailable,
                isClaudeAvailable,
                nowUnixMs)
            : new ProviderSelection(
                settings.ProviderMode == ProviderMode.Claude || (isClaudeAvailable && !isCodexAvailable)
                    ? UsageProvider.Claude
                    : UsageProvider.Codex,
                false);
        var (shown, other, otherProvider, isOtherAvailable) = selection.Provider == UsageProvider.Claude
            ? (this.claudeState, this.providerState, UsageProvider.Codex, isCodexAvailable)
            : (this.providerState, this.claudeState, UsageProvider.Claude, isClaudeAvailable);
        var summary = isOtherAvailable
            ? UsageOverlayStateReducer.CreateProviderSummary(otherProvider, other.Usage, other.ServiceHealth, nowUnixMs)
            : null;
        return (shown, new ProviderPresentation(selection.Provider, selection.IsLabelEmphasized, summary));
    }

    private void PublishCurrentState(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UsageOverlayState usageOverlayState;
        try
        {
            lock (this.sync)
            {
                var settings = this.readSettings();
                var nowUnixMs = this.clock.UnixTimeMilliseconds;
                var visibility = this.providerState.Visibility;
                var (shown, presentation) = this.claude is null
                    ? (this.providerState, null)
                    : this.SelectProvider(settings, visibility, nowUnixMs);
                usageOverlayState = UsageOverlayStateReducer.Reduce(
                    shown.Usage,
                    shown.TokenActivity,
                    shown.ServiceHealth,
                    shown.ServiceStatusDescription,
                    visibility,
                    settings,
                    nowUnixMs,
                    shown.ServiceAffectedComponents,
                    shown.ServiceAffectedGroups,
                    shown.ServiceAffectedComponentGroups,
                    presentation);
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
