namespace WindowsAiAssistant.Contracts.Models.Agent;

public interface IRuntimeSliceResult
{
    StepRuntimeEngagementDecision EngagementDecision { get; }
    AgentStepState? InitialState { get; }
    StepDecision? InitialDecision { get; }
    StepOutcome? Outcome { get; }
}
