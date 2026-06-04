namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class RespondActionHandler : IActionHandler
{
    public string ActionName => "respond";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExecuteMessageAction(action, ActionName));
    }

    internal static ActionResult ExecuteMessageAction(AgentAction action, string actionName)
    {
        if (!action.Parameters.TryGetValue("message", out var message) ||
            string.IsNullOrWhiteSpace(message))
        {
            return new ActionResult
            {
                Success = false,
                Message = $"'{actionName}' icin parameters.message gerekli."
            };
        }

        return new ActionResult
        {
            Success = true,
            Message = message.Trim()
        };
    }
}
