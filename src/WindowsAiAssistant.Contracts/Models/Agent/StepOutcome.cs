using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class StepOutcome
{
    public AgentStepState State { get; init; } = new();
    public StepDecision Decision { get; init; } = new();
    public StepFeedback? Feedback { get; init; }
    public IReadOnlyList<StepFeedback> FeedbackHistory { get; init; } = [];
    public StepSafetyDecision? SafetyDecision { get; init; }
    public StepContinuationDisposition Disposition { get; init; } = StepContinuationDisposition.Stop;
    public string Message { get; init; } = string.Empty;
}
