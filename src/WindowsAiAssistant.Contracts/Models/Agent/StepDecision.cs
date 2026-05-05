using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class StepDecision
{
    public StepContinuationDisposition Disposition { get; init; } = StepContinuationDisposition.Stop;
    public AgentAction? NextAction { get; init; }
    public string Message { get; init; } = string.Empty;
}
