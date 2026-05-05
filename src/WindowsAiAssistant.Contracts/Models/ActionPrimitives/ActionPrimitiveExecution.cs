namespace WindowsAiAssistant.Contracts.Models.ActionPrimitives;

public sealed class ActionPrimitiveExecutionRequest
{
    public string CorrelationId { get; init; } = string.Empty;
    public ActionPrimitive? Primitive { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string>? Metadata { get; init; }
}

public sealed class ActionPrimitiveExecutionResult
{
    public bool Success { get; init; }
    public ActionPrimitiveKind PrimitiveKind { get; init; }
    public string Message { get; init; } = string.Empty;
    public string OutputText { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string? BlockedReason { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string>? Metadata { get; init; }
}
