using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class MouseMoveActionHandler : IActionHandler
{
    public string ActionName => "mouse_move";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ActionParameterReader.TryGetInt(action, "x", out var x) ||
            !ActionParameterReader.TryGetInt(action, "y", out var y))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "mouse_move icin parameters.x ve parameters.y gerekli."
            });
        }

        MouseInput.MoveTo(x, y);
        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = $"Mouse move @ {x},{y}"
        });
    }
}
