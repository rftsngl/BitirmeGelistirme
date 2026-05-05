using WindowsAiAssistant.Contracts.Models.Agent;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Contracts.Models.Verification;

public sealed class VerificationPrimitiveSpec
{
    public VerificationPrimitiveKind Kind { get; init; }
    public bool Required { get; init; } = true;
    public string? Description { get; init; }
    public IDictionary<string, string>? Parameters { get; init; }
}

public sealed class VerificationRequest
{
    public string CorrelationId { get; init; } = string.Empty;
    public AgentAction? Action { get; init; }
    public ActionExecutionResult? ExecutionResult { get; init; }
    public AgentExecutionContext? ExecutionContext { get; init; }
    public IReadOnlyList<VerificationPrimitiveSpec> Specifications { get; init; } = [];
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string>? Metadata { get; init; }
}

public sealed class VerificationEvidence
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IDictionary<string, string>? Data { get; init; }
}

public sealed class VerificationPrimitiveResult
{
    public VerificationPrimitiveKind PrimitiveKind { get; init; }
    public VerificationStatus Status { get; init; } = VerificationStatus.Inconclusive;
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<VerificationEvidence> Evidence { get; init; } = [];
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class VerificationResult
{
    public VerificationStatus Status { get; init; } = VerificationStatus.Inconclusive;
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<VerificationPrimitiveResult> PrimitiveResults { get; init; } = [];
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsVerified => Status == VerificationStatus.Verified;
}