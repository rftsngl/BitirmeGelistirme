using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class SafetyObservationSummary
{
    public SafetyDisposition Disposition { get; init; } = SafetyDisposition.Allowed;
    public SafetyRiskLevel RiskLevel { get; init; } = SafetyRiskLevel.Low;
    public bool RequiresApproval { get; init; }
}