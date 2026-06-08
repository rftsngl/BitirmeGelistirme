namespace WindowsAiAssistant.Runtime.Automation;

internal static class StaTaskRunner
{
    public static Task RunAsync(Action action, CancellationToken cancellationToken = default) =>
        RunAsync<object?>(() =>
        {
            action();
            return null;
        }, cancellationToken);

    public static Task<T> RunAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                tcs.TrySetResult(func());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "WindowsAiAssistant-UIA-STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static void RunBlocking(Action action) =>
        RunBlocking<object?>(() =>
        {
            action();
            return null;
        });

    public static T RunBlocking<T>(Func<T> func)
    {
        Exception? error = null;
        T? result = default;
        var thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
            Name = "WindowsAiAssistant-STA-Blocking"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            throw error;
        }

        return result!;
    }
}
