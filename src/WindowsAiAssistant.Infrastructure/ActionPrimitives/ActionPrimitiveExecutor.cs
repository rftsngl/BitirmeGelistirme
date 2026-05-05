using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure.ActionPrimitives;

public sealed class ActionPrimitiveExecutor : IActionPrimitiveExecutor
{
    private readonly IReadOnlyList<IActionPrimitiveHandler> _handlers;

    public ActionPrimitiveExecutor(IEnumerable<IActionPrimitiveHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToList();
    }

    public async Task<ActionPrimitiveExecutionResult> ExecuteAsync(
        ActionPrimitiveExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var primitive = request.Primitive;
        if (primitive is null)
        {
            return CreateBlocked(
                ActionPrimitiveKind.Wait,
                "Action primitive executor blocked: primitive payload is missing.",
                "Primitive payload is missing.");
        }

        var matchingHandlers = _handlers
            .Where(candidate => candidate.CanHandle(primitive))
            .ToArray();

        if (matchingHandlers.Length == 0)
        {
            return CreateBlocked(
                primitive.Kind,
                $"Action primitive executor blocked: no handler registered for '{primitive.Kind}'.",
                "No handler registered for primitive kind.");
        }

        if (matchingHandlers.Length > 1)
        {
            return CreateBlocked(
                primitive.Kind,
                $"Action primitive executor blocked: multiple handlers matched '{primitive.Kind}'.",
                "Ambiguous handler resolution for primitive kind.");
        }

        var handler = matchingHandlers[0];

        try
        {
            return await handler.ExecuteAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ActionPrimitiveExecutionResult
            {
                Success = false,
                PrimitiveKind = primitive.Kind,
                Message = $"Action primitive executor failed: {ex.Message}",
                OutputText = "Real primitive execution failed.",
                ErrorCode = "PrimitiveHandlerException",
                BlockedReason = null,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["exceptionType"] = ex.GetType().Name,
                    ["exceptionMessage"] = ex.Message
                }
            };
        }
    }

    private static ActionPrimitiveExecutionResult CreateBlocked(
        ActionPrimitiveKind kind,
        string message,
        string blockedReason)
    {
        var now = DateTimeOffset.UtcNow;

        return new ActionPrimitiveExecutionResult
        {
            Success = false,
            PrimitiveKind = kind,
            Message = message,
            OutputText = "Blocked execution: primitive dispatch blocked.",
            ErrorCode = null,
            BlockedReason = blockedReason,
            StartedAtUtc = now,
            CompletedAtUtc = now
        };
    }
}
