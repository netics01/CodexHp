using System.Windows.Threading;
using CodexHp.App.Infrastructure;
using CodexHp.App.Tests.Presentation;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class DisplayEnvironmentWatcherTests
{
    [Fact]
    public void Bursty_display_notifications_are_coalesced_on_the_ui_dispatcher()
    {
        StaTest.Run(() =>
        {
            var refreshCount = 0;
            using var watcher = new DisplayEnvironmentWatcher(
                Dispatcher.CurrentDispatcher,
                () => refreshCount++,
                TimeSpan.FromMilliseconds(20),
                subscribeToSystemEvents: false);

            watcher.RequestRefresh();
            watcher.RequestRefresh();
            watcher.RequestRefresh();
            PumpDispatcher(TimeSpan.FromMilliseconds(100));

            Assert.Equal(1, refreshCount);
        });
    }

    [Fact]
    public void Temporary_display_recovery_condition_is_retried_until_it_clears()
    {
        StaTest.Run(() =>
        {
            var refreshCount = 0;
            using var watcher = new DisplayEnvironmentWatcher(
                Dispatcher.CurrentDispatcher,
                () => ++refreshCount == 1,
                TimeSpan.FromMilliseconds(10),
                subscribeToSystemEvents: false,
                retryInterval: TimeSpan.FromMilliseconds(10),
                maximumRetries: 2);

            watcher.RequestRefresh();
            PumpDispatcher(TimeSpan.FromMilliseconds(100));

            Assert.Equal(2, refreshCount);
        });
    }

    [Fact]
    public void Recovery_continues_at_a_slow_interval_after_the_fast_retry_budget_then_stops()
    {
        StaTest.Run(() =>
        {
            var calls = new List<long>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var watcher = new DisplayEnvironmentWatcher(
                Dispatcher.CurrentDispatcher,
                () => { calls.Add(clock.ElapsedMilliseconds); return calls.Count < 4; },
                TimeSpan.FromMilliseconds(5), subscribeToSystemEvents: false,
                retryInterval: TimeSpan.FromMilliseconds(5), maximumRetries: 1,
                slowRetryInterval: TimeSpan.FromMilliseconds(40));

            watcher.RequestRefresh();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (calls.Count < 4 && DateTime.UtcNow < deadline)
            {
                PumpDispatcher(TimeSpan.FromMilliseconds(20));
            }
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            Assert.Equal(4, calls.Count);
            Assert.True(calls[2] - calls[1] >= 30);
            Assert.True(calls[3] - calls[2] >= 30);
        });
    }

    [Fact]
    public void Disposing_a_pending_recovery_stops_all_further_attempts()
    {
        StaTest.Run(() =>
        {
            var calls = 0;
            var watcher = new DisplayEnvironmentWatcher(Dispatcher.CurrentDispatcher,
                () => { calls++; return true; }, TimeSpan.FromMilliseconds(5),
                subscribeToSystemEvents: false, maximumRetries: 0,
                slowRetryInterval: TimeSpan.FromMilliseconds(20));
            watcher.RequestRefresh();
            PumpDispatcher(TimeSpan.FromMilliseconds(60));
            watcher.Dispose();
            var stoppedAt = calls;
            Assert.True(stoppedAt > 0);
            PumpDispatcher(TimeSpan.FromMilliseconds(80));
            Assert.Equal(stoppedAt, calls);
        });
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            duration,
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
