namespace WindowsAiAssistant.Runtime.Actions;

public sealed class AgentAction
{
    public required string Action { get; init; }
    public string? Target { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
