namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class CapabilityInvocation
{
    public required string RoutedCapabilityName { get; init; }
    public required TargetReference Target { get; init; }
    public required AgentAction Action { get; init; }
    public required string RoutingSource { get; init; }
}
