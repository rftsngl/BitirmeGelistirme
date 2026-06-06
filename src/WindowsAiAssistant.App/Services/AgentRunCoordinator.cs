namespace WindowsAiAssistant.App.Services;

/// <summary>
/// Tek seferde yalnızca bir agent run (ana UI veya overlay) çalışsın.
/// </summary>
public sealed class AgentRunCoordinator
{
    private int _busy;

    public bool IsRunActive => Volatile.Read(ref _busy) == 1;

    public bool TryEnterRun() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    public void ExitRun() => Interlocked.Exchange(ref _busy, 0);
}
