namespace WindowsAiAssistant.Contracts.Models;

public enum SafetyDisposition
{
    Allowed,
    Denied,
    RequiresApproval
}

public enum SafetyRiskLevel
{
    Low,
    Medium,
    High,
    VeryHigh
}

public sealed class SafetyDecision
{
    public SafetyDisposition Disposition { get; init; } = SafetyDisposition.Denied;
    public SafetyRiskLevel RiskLevel { get; init; } = SafetyRiskLevel.High;
    public string Reason { get; init; } = string.Empty;
}
