using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ISafetyGate
{
    Task<SafetyDecision> EvaluateAsync(CommandRequest request, CancellationToken cancellationToken = default);
}
