using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;
using WindowsAiAssistant.Contracts.Models.Verification;

namespace WindowsAiAssistant.Contracts.Models.Agent;

public sealed class ActionExecutionResult
{
    public ExecutionStatus Status { get; init; } = ExecutionStatus.Planned;
    public string Message { get; init; } = string.Empty;
    public bool IsVerified { get; init; }
    public string? OutputText { get; init; }
    public IDictionary<string, string>? OutputData { get; init; }
    public string? ErrorCode { get; init; }
    public bool UsedFallback { get; init; }
    public ActionPrimitiveExecutionResult? PrimitiveExecution { get; init; }
    public VerificationResult? Verification { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
