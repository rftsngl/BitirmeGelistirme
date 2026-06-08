namespace WindowsAiAssistant.App.Services;

internal static class SafeFireAndForget
{
    public static void Run(Func<Task> work, string context)
    {
        ArgumentNullException.ThrowIfNull(work);
        _ = RunInternal(work, context);
    }

    private static async Task RunInternal(Func<Task> work, string context)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, context);
        }
    }
}
