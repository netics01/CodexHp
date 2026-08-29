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
