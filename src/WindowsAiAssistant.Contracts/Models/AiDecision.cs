namespace WindowsAiAssistant.Contracts.Models;

public sealed class AiDecision
{
    public string SelectedToolName { get; init; } = string.Empty;
    public string? ToolArgument { get; init; }
    public string DecisionText { get; init; } = string.Empty;
    public NextActionDecision? NextActionDecision { get; init; }
    public string? RoutedCapabilityName { get; init; }
    public string? RoutedActionName { get; init; }
    public bool IsLegacyFallback { get; init; }
}
