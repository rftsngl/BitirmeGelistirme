using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class MouseDragActionHandler : IActionHandler
{
    public string ActionName => "mouse_drag";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ActionParameterReader.TryGetInt(action, "startX", out var startX) ||
            !ActionParameterReader.TryGetInt(action, "startY", out var startY) ||
            !ActionParameterReader.TryGetInt(action, "endX", out var endX) ||
            !ActionParameterReader.TryGetInt(action, "endY", out var endY))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "mouse_drag icin parameters.startX, startY, endX, endY (tamsayi) gerekli."
            });
        }

        MouseInput.Drag(startX, startY, endX, endY);
        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = $"Mouse drag {startX},{startY} -> {endX},{endY}"
        });
    }
}
