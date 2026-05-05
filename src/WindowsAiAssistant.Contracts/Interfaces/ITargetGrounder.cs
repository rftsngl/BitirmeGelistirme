using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ITargetGrounder
{
    Task<TargetGroundingResult> GroundAsync(
        CommandRequest request,
        IReadOnlyList<TargetReference> resolvedTargets,
        CancellationToken cancellationToken = default);
}
