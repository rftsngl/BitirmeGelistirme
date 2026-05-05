using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IStepRuntimeEngagementBoundary
{
    StepRuntimeEngagementDecision EvaluateEngagement(AgentStepState state, AgentAction proposedAction);
}
