using CodexHp.App.Application;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class UpdateMonitorTests
{
    [Fact]
    public async Task Checks_at_start_and_every_seven_days_and_catches_up_only_once_after_sleep()
    {
        var clock = new Clock();
        var calls = 0;
        var monitor = new UpdateMonitor(_ => { calls++; return Task.FromResult<AvailableUpdate?>(null); }, clock, new Logger());
        await monitor.CheckIfDueAsync(default);
        clock.Now += (long)TimeSpan.FromDays(7).TotalMilliseconds - 1;
        await monitor.CheckIfDueAsync(default);
        Assert.Equal(1, calls);
        clock.Now++;
        await monitor.CheckIfDueAsync(default);
        Assert.Equal(2, calls);
        clock.Now += (long)TimeSpan.FromDays(30).TotalMilliseconds;
        await monitor.CheckIfDueAsync(default);
        await monitor.CheckIfDueAsync(default);
        Assert.Equal(3, calls);
        var restarted = new UpdateMonitor(_ => { calls++; return Task.FromResult<AvailableUpdate?>(null); }, clock, new Logger());
        await restarted.CheckIfDueAsync(default);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Failure_keeps_known_update_and_does_not_retry_immediately()
    {
        var clock = new Clock();
        var logger = new Logger();
        var release = AvailableUpdate.FromTag("v0.5.0");
        var calls = 0;
        var monitor = new UpdateMonitor(_ => ++calls == 1 ? Task.FromResult<AvailableUpdate?>(release)
            : Task.FromException<AvailableUpdate?>(new System.Net.Http.HttpRequestException("Offline")), clock, logger);
        var notifications = 0;
        monitor.Changed += _ => notifications++;
        await monitor.CheckIfDueAsync(default);
        clock.Now += (long)UpdateMonitor.Interval.TotalMilliseconds;
        await monitor.CheckIfDueAsync(default);
        await monitor.CheckIfDueAsync(default);
        Assert.Equal(release, monitor.Available);
        Assert.True(monitor.LastCheckFailed);
        Assert.Equal(2, calls);
        Assert.Equal(1, notifications);
        Assert.Equal(1, logger.Warnings);
    }

    [Fact]
    public async Task Successful_current_result_clears_old_update_but_cancellation_never_publishes()
    {
        var clock = new Clock();
        AvailableUpdate? next = AvailableUpdate.FromTag("v0.5.0");
        var monitor = new UpdateMonitor(_ => Task.FromResult<AvailableUpdate?>(next), clock, new Logger());
        await monitor.CheckIfDueAsync(default);
        next = null;
        clock.Now += (long)UpdateMonitor.Interval.TotalMilliseconds;
        await monitor.CheckIfDueAsync(default);
        Assert.Null(monitor.Available);
        using var stop = new CancellationTokenSource();
        var late = new UpdateMonitor(_ => { stop.Cancel(); return Task.FromResult<AvailableUpdate?>(AvailableUpdate.FromTag("v0.5.0")); }, clock, new Logger());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => late.CheckIfDueAsync(stop.Token));
        Assert.Null(late.Available);
    }

    [Fact]
    public async Task Concurrent_checks_do_not_overlap_and_running_loop_stops_on_shutdown()
    {
        var pending = new TaskCompletionSource<AvailableUpdate?>();
        var calls = 0;
        var clock = new Clock();
        var monitor = new UpdateMonitor(_ => { calls++; return pending.Task; }, clock, new Logger());
        var first = monitor.CheckIfDueAsync(default);
        clock.Now += (long)UpdateMonitor.Interval.TotalMilliseconds;
        await monitor.CheckIfDueAsync(default);
        Assert.Equal(1, calls);
        pending.SetResult(null);
        await first;
        using var stop = new CancellationTokenSource();
        var loop = monitor.RunAsync(stop.Token);
        stop.Cancel();
        await loop.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class Clock : IClock
    {
        internal long Now = 1_000_000;
        public long UnixTimeMilliseconds => this.Now;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
    private sealed class Logger : IDiagnosticLogger
    {
        internal int Warnings;
        public void Log(DiagnosticLevel level, string component, string message, Exception? exception = null) => this.Warnings++;
    }
}
