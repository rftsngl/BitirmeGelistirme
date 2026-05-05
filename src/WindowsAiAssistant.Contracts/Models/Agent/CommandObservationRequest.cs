using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class CommandObservationRequest
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string OriginalUserCommand { get; init; } = string.Empty;
    public ObservationSnapshot? BaselineObservation { get; init; }
    public TargetGroundingResult? PrimaryTargetGrounding { get; init; }
    public SafetyDisposition SafetyDisposition { get; init; } = SafetyDisposition.Allowed;
    public SafetyRiskLevel SafetyRiskLevel { get; init; } = SafetyRiskLevel.Low;
}