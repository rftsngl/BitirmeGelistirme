using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ServiceRuntimeSliceResult : IRuntimeSliceResult
{
    public StepRuntimeEngagementDecision EngagementDecision { get; init; } = new()
    {
        Disposition = StepRuntimeEngagementDisposition.Decline,
        Message = "Narrow service runtime slice did not engage."
    };

    public AgentStepState? InitialState { get; init; }
    public StepDecision? InitialDecision { get; init; }
    public StepOutcome? Outcome { get; init; }
}
