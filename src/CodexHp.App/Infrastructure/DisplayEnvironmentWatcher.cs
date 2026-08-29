using System.Windows.Threading;
using Microsoft.Win32;

namespace CodexHp.App.Infrastructure;

public sealed class DisplayEnvironmentWatcher : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Action refresh;
    private readonly DispatcherTimer timer;
    private readonly bool subscribedToSystemEvents;
    private bool isDisposed;

    public DisplayEnvironmentWatcher(
        Dispatcher dispatcher,
        Action refresh,
        TimeSpan? debounceInterval = null,
        bool subscribeToSystemEvents = true)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        this.timer = new DispatcherTimer(
            debounceInterval ?? TimeSpan.FromMilliseconds(350),
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
            this.refresh();
        }
    }
}
