namespace WindowsAiAssistant.Runtime.Session;

/// <summary>
/// Per agent-run context for orchestration correlation (approvals, audit logs).
/// </summary>
public sealed class AgentRunScope : IDisposable
{
    private static readonly AsyncLocal<AgentRunScope?> CurrentScope = new();
    private readonly AgentRunScope? _previous;

    public static AgentRunScope? Current => CurrentScope.Value;

    public AgentRunScope(string runId, string userGoal, string? triggerSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userGoal);
        RunId = runId;
        UserGoal = userGoal.Trim();
        TriggerSource = string.IsNullOrWhiteSpace(triggerSource) ? null : triggerSource.Trim();
        StartedAtUtc = DateTimeOffset.UtcNow;
        _previous = CurrentScope.Value;
        CurrentScope.Value = this;
    }

    public string RunId { get; }
    public string UserGoal { get; }
    public string? TriggerSource { get; }
    public DateTimeOffset StartedAtUtc { get; }

    public void Dispose() => CurrentScope.Value = _previous;
}
