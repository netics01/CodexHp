namespace CodexHp.App.Application;

internal sealed class UpdateMonitor(
    Func<CancellationToken, Task<AvailableUpdate?>> fetch,
    IClock clock,
    IDiagnosticLogger logger)
{
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(7);
    private long nextCheckUnixMs = long.MinValue;
    private int checking;
    internal AvailableUpdate? Available { get; private set; }
    internal bool LastCheckFailed { get; private set; }
    internal event Action<AvailableUpdate?>? Changed;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await this.CheckIfDueAsync(cancellationToken).ConfigureAwait(false);
                // Re-evaluate wall time after sleep/resume; never replay missed intervals.
                await clock.DelayAsync(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal async Task CheckIfDueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clock.UnixTimeMilliseconds < this.nextCheckUnixMs || Interlocked.Exchange(ref this.checking, 1) != 0) return;
        try
        {
            this.nextCheckUnixMs = clock.UnixTimeMilliseconds + (long)Interval.TotalMilliseconds;
            AvailableUpdate? result;
            try
            {
                result = await fetch(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                this.LastCheckFailed = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                this.LastCheckFailed = true;
                logger.Log(DiagnosticLevel.Warning, "Updates", "GitHub release check failed; retaining the last known update.", exception);
                return;
            }
            if (result != this.Available)
            {
                this.Available = result;
                this.Changed?.Invoke(result);
            }
        }
        finally { Volatile.Write(ref this.checking, 0); }
    }
}
