using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IStepEntryDecider
{
    StepDecision DecideInitialStep(AgentStepState state);
}
