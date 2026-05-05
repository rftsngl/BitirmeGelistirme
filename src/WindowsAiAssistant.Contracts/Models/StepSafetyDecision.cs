using WindowsAiAssistant.Contracts.Models.Agent;

namespace WindowsAiAssistant.Contracts.Models;

public enum StepSafetyRequestSource
{
    DecisionLoop = 0,
    StepRuntime = 1
}

public sealed class StepSafetyRequest
{
    public StepSafetyRequestSource Source { get; init; } = StepSafetyRequestSource.DecisionLoop;
    public string CommandText { get; init; } = string.Empty;
    public int StepIndex { get; init; }
    public ExecuteActionPayload? ExecuteAction { get; init; }
    public AgentAction? AgentAction { get; init; }
}

public sealed class StepSafetyDecision
{
    public SafetyDisposition Disposition { get; init; } = SafetyDisposition.Allowed;
    public SafetyRiskLevel RiskLevel { get; init; } = SafetyRiskLevel.Low;
    public string Reason { get; init; } = string.Empty;
    public string? ApprovalKey { get; init; }
}
