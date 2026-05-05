using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IStepFollowUpDecider
{
    StepDecision DecideNextStep(AgentStepState state, StepFeedback feedback);
}
