using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Models;

public sealed class CommandRequest
{
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public string UserInput { get; init; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public AiDecisionInput? AiDecisionInput { get; init; }
    public ModelFacingObservationPackage? ModelObservationPackage { get; init; }
    public TargetGroundingResult? PrimaryTargetGrounding { get; init; }
}
