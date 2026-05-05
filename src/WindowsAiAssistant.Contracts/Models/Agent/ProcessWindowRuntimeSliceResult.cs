using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ProcessWindowRuntimeSliceResult : IRuntimeSliceResult
{
    public StepRuntimeEngagementDecision EngagementDecision { get; init; } = new()
    {
        Disposition = StepRuntimeEngagementDisposition.Decline,
        Message = "Narrow process/window runtime slice did not engage."
    };

    public AgentStepState? InitialState { get; init; }
    public StepDecision? InitialDecision { get; init; }
    public StepOutcome? Outcome { get; init; }
}
