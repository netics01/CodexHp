using System.Runtime.ExceptionServices;

namespace CodexHp.App.Tests.Presentation;

internal static class StaTest
{
    internal static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
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
