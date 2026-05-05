using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IStepSafetyEvaluator
{
    Task<StepSafetyDecision> EvaluateAsync(StepSafetyRequest request, CancellationToken cancellationToken = default);
}
