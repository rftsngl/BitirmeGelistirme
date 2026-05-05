using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class TargetGroundingResult
{
    public TargetGroundingDisposition Disposition { get; init; } = TargetGroundingDisposition.Unresolved;
    public TargetGroundingInputSource InputSource { get; init; } = TargetGroundingInputSource.RawInputFallback;
    public GroundedTarget Target { get; init; } = new();
    public TargetExecutionSuitability ExecutionSuitability { get; init; } = TargetExecutionSuitability.NotExecutable;
    public TargetGroundingReason Reason { get; init; } = TargetGroundingReason.None;
    public string Message { get; init; } = string.Empty;
}
