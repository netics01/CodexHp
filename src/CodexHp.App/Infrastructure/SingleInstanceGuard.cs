namespace CodexHp.App.Infrastructure;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = "Local\\CodexHp.SingleInstance";
    private readonly Mutex mutex;
    private bool disposed;

    private SingleInstanceGuard(Mutex mutex)
    {
        this.mutex = mutex;
    }

    public static SingleInstanceGuard? TryAcquire(string? mutexName = null)
    {
        var name = string.IsNullOrWhiteSpace(mutexName) ? DefaultMutexName : mutexName;
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        try
        {
            this.mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        this.mutex.Dispose();
    }
}
