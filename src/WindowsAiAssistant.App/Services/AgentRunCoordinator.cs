namespace WindowsAiAssistant.App.Services;

/// <summary>
/// Tek seferde yalnızca bir agent run (ana UI veya overlay) çalışsın.
/// </summary>
public sealed class AgentRunCoordinator
{
    private int _busy;
    private string? _activeRunId;

    public event Action? RunEnded;

    public bool IsRunActive => Volatile.Read(ref _busy) == 1;

    public string? ActiveRunId => _activeRunId;

    public bool TryEnterRun(string? runId = null)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return false;
        }

        _activeRunId = runId;
        return true;
    }

    public void ExitRun()
    {
        Interlocked.Exchange(ref _busy, 0);
        _activeRunId = null;
        RunEnded?.Invoke();
    }
}
