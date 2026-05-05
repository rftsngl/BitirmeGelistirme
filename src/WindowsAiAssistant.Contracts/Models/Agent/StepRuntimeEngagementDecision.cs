using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class StepRuntimeEngagementDecision
{
    public StepRuntimeEngagementDisposition Disposition { get; init; } = StepRuntimeEngagementDisposition.Decline;
    public string Message { get; init; } = string.Empty;
}
