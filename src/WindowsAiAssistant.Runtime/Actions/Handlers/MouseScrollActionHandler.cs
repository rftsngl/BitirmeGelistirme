using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class MouseScrollActionHandler : IActionHandler
{
    public string ActionName => "mouse_scroll";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var delta = 120;
        if (ActionParameterReader.TryGetInt(action, "delta", out var parsed))
        {
            delta = parsed;
        }
        else if (action.Parameters.TryGetValue("direction", out var direction) &&
                 !string.IsNullOrWhiteSpace(direction))
        {
            delta = direction.Trim().ToLowerInvariant() switch
            {
                "up" => 120,
                "down" => -120,
                _ => delta
            };
        }

        MouseInput.Scroll(delta);
        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = $"Mouse scroll delta={delta}"
        });
    }
}
