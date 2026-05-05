using WindowsAiAssistant.Contracts.Models.ActionPrimitives;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ExecutionRuntimeState
{
    public string? CapabilityName { get; init; }
    public string? ActionName { get; init; }
    public ExecutionStatus? ActionStatus { get; init; }
    public VerificationStatus? VerificationStatus { get; init; }
    public string? VerificationReason { get; init; }
    public TargetKind? TargetKind { get; init; }
    public string? TargetValue { get; init; }
    public string? BlockedReason { get; init; }
    public string? FailureReason { get; init; }
    public ActionPrimitiveKind? PrimitiveKind { get; init; }
    public bool? PrimitiveSucceeded { get; init; }
}
