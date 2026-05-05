using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class GroundedTarget
{
    public GroundedTargetKind Kind { get; init; } = GroundedTargetKind.Unknown;
    public string OriginalText { get; init; } = string.Empty;
    public string CanonicalValue { get; init; } = string.Empty;
    public double Confidence { get; init; }
}
