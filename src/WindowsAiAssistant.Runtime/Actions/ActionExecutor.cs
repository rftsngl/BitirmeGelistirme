namespace WindowsAiAssistant.Runtime.Actions;

public sealed class ActionExecutor
{
    private readonly IReadOnlyDictionary<string, IActionHandler> _handlers;

    public ActionExecutor(IEnumerable<IActionHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(handler => handler.ActionName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_handlers.TryGetValue(action.Action, out var handler))
        {
            return new ActionResult
            {
                Success = false,
                ErrorCode = ActionFailureCodes.UnsupportedAction,
                Message = $"Desteklenmeyen action: '{action.Action}'."
            };
        }

        try
        {
            return await handler.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ActionResult
            {
                Success = false,
                ErrorCode = ActionFailureCodes.HandlerException,
                ExceptionType = ex.GetType().Name,
                Message = $"Action hatasi ({action.Action}): {SanitizeUserMessage(ex.Message)}"
            };
        }
    }

    private static string SanitizeUserMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Bilinmeyen hata.";
        }

        var oneLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 240 ? oneLine : oneLine[..240];
    }
}
