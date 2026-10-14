namespace CodexHp.App.Infrastructure;

// Reports foreground-window changes as they happen so the shown provider can follow the active
// app without waiting for the next visibility poll. Create and dispose it on a thread with a message loop.
public sealed class ForegroundWindowWatcher : IDisposable
{
    private readonly Action changed;
    private readonly NativeMethods.WinEventProc callback;
    private nint hook;

    public ForegroundWindowWatcher(Action changed)
    {
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
        // Keep the delegate referenced for the lifetime of the hook.
        this.callback = this.OnWinEvent;
        this.hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            nint.Zero,
            this.callback,
            0,
            0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);
    }

    // False when Windows refused the hook; the regular visibility poll still follows the active app.
    public bool IsActive => this.hook != nint.Zero;

    public void Dispose()
    {
        if (this.hook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(this.hook);
            this.hook = nint.Zero;
        }
    }

    private void OnWinEvent(
        nint hookHandle,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTimeMs) => this.changed();
}
