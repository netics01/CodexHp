using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class OpenAiServiceStatusPollerTests
{
    [Fact]
    public async Task ReadAsync_reuses_successful_status_for_three_minutes()
    {
        var now = 1_780_000_000_000L;
        var fetchCount = 0;
        var poller = new OpenAiServiceStatusPoller(
            fetchStatusAsync: _ =>
            {
                fetchCount++;
                return Task.FromResult(Operational(now));
            },
            unixMsClock: () => now);

        var first = await poller.ReadAsync();
        now += (long)TimeSpan.FromMinutes(2).TotalMilliseconds;
        var second = await poller.ReadAsync();

        Assert.Same(first, second);
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task ReadAsync_refetches_at_three_minute_boundary()
    {
        var now = 1_780_000_000_000L;
        var fetchCount = 0;
        var poller = new OpenAiServiceStatusPoller(
            fetchStatusAsync: _ => Task.FromResult(Operational(now, (++fetchCount).ToString())),
            unixMsClock: () => now);

        await poller.ReadAsync();
        now += (long)TimeSpan.FromMinutes(3).TotalMilliseconds;
        var second = await poller.ReadAsync();

        Assert.Equal("2", second.Description);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task ReadAsync_retries_one_minute_after_failure()
    {
        var now = 1_780_000_000_000L;
        var fail = true;
        var fetchCount = 0;
        var poller = new OpenAiServiceStatusPoller(
            fetchStatusAsync: _ =>
            {
                fetchCount++;
                if (fail)
                {
                    throw new HttpRequestException("status failed");
                }

                return Task.FromResult(Operational(now));
            },
            unixMsClock: () => now);

        var failed = await poller.ReadAsync();
        now += (long)TimeSpan.FromSeconds(59).TotalMilliseconds;
        var cached = await poller.ReadAsync();
        fail = false;
        now += (long)TimeSpan.FromSeconds(1).TotalMilliseconds;
        var recovered = await poller.ReadAsync();

        Assert.Equal(ServiceHealthState.Unknown, failed.Health);
        Assert.Same(failed, cached);
        Assert.Equal(ServiceHealthState.Operational, recovered.Health);
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task ReadAsync_propagates_requested_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var poller = new OpenAiServiceStatusPoller(
            fetchStatusAsync: token => Task.FromCanceled<OpenAiServiceStatusSnapshot>(token),
            unixMsClock: () => 1_780_000_000_000L);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => poller.ReadAsync(cancellation.Token));
    }

    private static OpenAiServiceStatusSnapshot Operational(long now, string description = "ok") =>
        new(ServiceHealthState.Operational, "none", description, now);
}
