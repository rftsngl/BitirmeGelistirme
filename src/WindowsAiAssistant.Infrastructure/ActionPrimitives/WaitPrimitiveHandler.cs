using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure.ActionPrimitives;

public sealed class WaitPrimitiveHandler : IActionPrimitiveHandler
{
    private static readonly TimeSpan MaxAllowedDuration = TimeSpan.FromMinutes(5);

    public bool CanHandle(ActionPrimitive primitive)
    {
        return primitive.Kind == ActionPrimitiveKind.Wait;
    }

    public async Task<ActionPrimitiveExecutionResult> ExecuteAsync(
        ActionPrimitiveExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (request.Primitive is not WaitPrimitive waitPrimitive)
        {
            return CreateBlocked(
                startedAt,
                "Wait primitive blocked: invalid payload type.",
                "Primitive payload did not match Wait.");
        }

        var duration = waitPrimitive.Duration;
        if (duration <= TimeSpan.Zero)
        {
            return CreateBlocked(
                startedAt,
                "Wait primitive blocked: duration must be greater than zero.",
                "Wait duration must be greater than zero.");
        }

        if (duration > MaxAllowedDuration)
        {
            return CreateBlocked(
                startedAt,
                $"Wait primitive blocked: duration exceeds maximum ({MaxAllowedDuration}).",
                "Wait duration exceeds maximum allowed limit.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["duration_ms"] = duration.TotalMilliseconds.ToString("F0"),
                    ["max_duration_ms"] = MaxAllowedDuration.TotalMilliseconds.ToString("F0")
                });
        }

        try
        {
            await Task.Delay(duration, cancellationToken);

            return new ActionPrimitiveExecutionResult
            {
                Success = true,
                PrimitiveKind = ActionPrimitiveKind.Wait,
                Message = "Wait primitive completed successfully.",
                OutputText = $"Waited for {duration.TotalMilliseconds:F0} ms.",
                ErrorCode = null,
                BlockedReason = null,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["duration_ms"] = duration.TotalMilliseconds.ToString("F0")
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ActionPrimitiveExecutionResult
            {
                Success = false,
                PrimitiveKind = ActionPrimitiveKind.Wait,
                Message = "Wait primitive cancelled.",
                OutputText = "Wait execution cancelled.",
                ErrorCode = "Cancelled",
                BlockedReason = null,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["duration_ms"] = duration.TotalMilliseconds.ToString("F0")
                }
            };
        }
    }

    private static ActionPrimitiveExecutionResult CreateBlocked(
        DateTimeOffset startedAt,
        string message,
        string blockedReason,
        IDictionary<string, string>? metadata = null)
    {
        return new ActionPrimitiveExecutionResult
        {
            Success = false,
            PrimitiveKind = ActionPrimitiveKind.Wait,
            Message = message,
            OutputText = "Blocked execution: wait primitive blocked.",
            ErrorCode = null,
            BlockedReason = blockedReason,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Metadata = metadata
        };
    }
}
