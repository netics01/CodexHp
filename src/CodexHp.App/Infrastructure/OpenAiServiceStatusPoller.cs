namespace CodexHp.App.Infrastructure;

public sealed class OpenAiServiceStatusPoller
{
    private static readonly TimeSpan SuccessInterval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(1);
    private readonly Func<CancellationToken, Task<OpenAiServiceStatusSnapshot>> fetchStatusAsync;
    private readonly Func<long> unixMsClock;
    private OpenAiServiceStatusSnapshot? cachedStatus;
    private long nextFetchUnixMs;

    public OpenAiServiceStatusPoller(
        Func<CancellationToken, Task<OpenAiServiceStatusSnapshot>> fetchStatusAsync,
        Func<long> unixMsClock)
    {
        this.fetchStatusAsync = fetchStatusAsync ?? throw new ArgumentNullException(nameof(fetchStatusAsync));
        this.unixMsClock = unixMsClock ?? throw new ArgumentNullException(nameof(unixMsClock));
    }

    public async Task<OpenAiServiceStatusSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var nowUnixMs = this.unixMsClock();
        if (this.cachedStatus is not null && nowUnixMs < this.nextFetchUnixMs)
        {
            return this.cachedStatus;
        }

        try
        {
            this.cachedStatus = await this.fetchStatusAsync(cancellationToken);
            this.nextFetchUnixMs = nowUnixMs + (long)SuccessInterval.TotalMilliseconds;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            this.cachedStatus = OpenAiServiceStatusSnapshot.Unknown(nowUnixMs);
            this.nextFetchUnixMs = nowUnixMs + (long)FailureRetryInterval.TotalMilliseconds;
        }

        return this.cachedStatus;
    }
}
