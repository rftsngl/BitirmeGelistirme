namespace WindowsAiAssistant.Agent;

public sealed class AgentSession
{
    public required string RunId { get; init; }
    public required string UserGoal { get; init; }
    public IList<AgentStep> Steps { get; } = [];
    public bool IsComplete { get; set; }

    private readonly Dictionary<string, int> _actionFailCounts = new(StringComparer.OrdinalIgnoreCase);

    public int RecordActionFailure(string action, string? target)
    {
        var key = BuildFailureKey(action, target);
        _actionFailCounts.TryGetValue(key, out var count);
        count++;
        _actionFailCounts[key] = count;
        return count;
    }

    public void ResetActionFailure(string action, string? target) =>
        _actionFailCounts.Remove(BuildFailureKey(action, target));

    internal static string BuildFailureKey(string action, string? target) =>
        $"{action.Trim()}|{(target ?? string.Empty).Trim()}".ToLowerInvariant();
}
