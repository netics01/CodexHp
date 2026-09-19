using System.Runtime.ExceptionServices;

namespace CodexHp.App.Tests.Presentation;

internal static class StaTest
{
    // WPF theme/BAML caches are shared across the process. Separate STA threads
    // must not initialize controls concurrently, even across xUnit test classes.
    private static readonly object ExecutionGate = new();

    internal static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (ExecutionGate)
        {
            RunOnStaThread(action);
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
