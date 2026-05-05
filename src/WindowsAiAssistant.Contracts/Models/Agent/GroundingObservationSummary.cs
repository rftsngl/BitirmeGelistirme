using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class GroundingObservationSummary
{
    public TargetGroundingDisposition Disposition { get; init; } = TargetGroundingDisposition.Unresolved;
    public TargetGroundingInputSource InputSource { get; init; } = TargetGroundingInputSource.RawInputFallback;
    public GroundedTargetKind TargetKind { get; init; } = GroundedTargetKind.Unknown;
    public string? CanonicalValue { get; init; }
    public TargetGroundingReason Reason { get; init; } = TargetGroundingReason.None;
}