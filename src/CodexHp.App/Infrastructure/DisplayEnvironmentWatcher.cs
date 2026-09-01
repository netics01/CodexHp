using System.Windows.Threading;
using Microsoft.Win32;

namespace CodexHp.App.Infrastructure;

public sealed class DisplayEnvironmentWatcher : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Func<bool> refresh;
    private readonly DispatcherTimer timer;
    private readonly bool subscribedToSystemEvents;
    private readonly TimeSpan debounceInterval;
    private readonly TimeSpan retryInterval;
    private readonly int maximumRetries;
    private int retryCount;
    private bool isDisposed;

    public DisplayEnvironmentWatcher(
        Dispatcher dispatcher,
        Action refresh,
        TimeSpan? debounceInterval = null,
        bool subscribeToSystemEvents = true)
        : this(
            dispatcher,
            WrapRefresh(refresh),
            debounceInterval,
            subscribeToSystemEvents)
    {
    }

    public DisplayEnvironmentWatcher(
        Dispatcher dispatcher,
        Func<bool> refresh,
        TimeSpan? debounceInterval = null,
        bool subscribeToSystemEvents = true,
        TimeSpan? retryInterval = null,
        int maximumRetries = 8)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        this.debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(350);
        this.retryInterval = retryInterval ?? TimeSpan.FromMilliseconds(250);
        if (this.debounceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceInterval));
        }

        if (this.retryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryInterval));
        }

        if (maximumRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRetries));
        }

        this.maximumRetries = maximumRetries;
        this.timer = new DispatcherTimer(
            this.debounceInterval,
            DispatcherPriority.Background,
            this.OnTimerTick,
            dispatcher);
        this.timer.Stop();
        this.subscribedToSystemEvents = subscribeToSystemEvents;
        if (subscribeToSystemEvents)
        {
            SystemEvents.DisplaySettingsChanged += this.OnSystemDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += this.OnUserPreferenceChanged;
        }
    }

    public void RequestRefresh()
    {
        if (this.isDisposed)
        {
            return;
        }

        if (!this.dispatcher.CheckAccess())
        {
            _ = this.dispatcher.BeginInvoke(this.RequestRefresh);
            return;
        }

        this.timer.Stop();
        this.timer.Interval = this.debounceInterval;
        this.retryCount = 0;
        this.timer.Start();
    }

    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
        this.timer.Stop();
        if (this.subscribedToSystemEvents)
        {
            SystemEvents.DisplaySettingsChanged -= this.OnSystemDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= this.OnUserPreferenceChanged;
        }
    }

    private void OnSystemDisplaySettingsChanged(object? sender, EventArgs eventArgs) =>
        this.RequestRefresh();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs) =>
        this.RequestRefresh();

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        this.timer.Stop();
        if (!this.isDisposed)
        {
            var requiresRetry = this.refresh();
            if (requiresRetry && this.retryCount < this.maximumRetries)
            {
                this.retryCount++;
                this.timer.Interval = this.retryInterval;
                this.timer.Start();
                return;
            }

            this.retryCount = 0;
            this.timer.Interval = this.debounceInterval;
        }
    }

    private static Func<bool> WrapRefresh(Action refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        return () =>
        {
            refresh();
            return false;
        };
    }
}
