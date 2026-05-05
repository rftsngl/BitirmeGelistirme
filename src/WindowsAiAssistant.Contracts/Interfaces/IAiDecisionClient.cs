using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IAiDecisionClient
{
    Task<AiDecision> DecideAsync(CommandRequest request, CancellationToken cancellationToken = default);
}
