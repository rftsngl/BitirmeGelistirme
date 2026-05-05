namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class AgentAction
{
    public string CapabilityName { get; init; } = string.Empty;
    public string ActionName { get; init; } = string.Empty;
    public TargetReference? Target { get; init; }
    public IDictionary<string, string>? Parameters { get; init; }
    public bool RequiresForeground { get; init; }
    public bool RequiresObservation { get; init; }
    public string? Notes { get; init; }
}
