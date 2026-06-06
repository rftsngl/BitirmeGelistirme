using WindowsAiAssistant.Runtime.Automation;
using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class MouseClickActionHandler : IActionHandler
{
    private readonly UiElementRegistry _registry;

    public MouseClickActionHandler(UiElementRegistry registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public string ActionName => "mouse_click";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        int x;
        int y;

        if (!string.IsNullOrWhiteSpace(elementId))
        {
            if (!_registry.TryGet(elementId, out var reference) || reference is null)
            {
                return Task.FromResult(new ActionResult
                {
                    Success = false,
                    Message = $"Element bulunamadi: '{elementId}'. Gozlem yenilendi; gecerli elementId kullanin."
                });
            }

            x = reference.X + reference.Width / 2;
            y = reference.Y + reference.Height / 2;
        }
        else if (ActionParameterReader.TryGetInt(action, "x", out x) &&
                 ActionParameterReader.TryGetInt(action, "y", out y))
        {
            // explicit coordinates
        }
        else
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "mouse_click icin parameters.elementId veya parameters.x + parameters.y gerekli."
            });
        }

        MouseInput.Click(x, y);
        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = $"Mouse click @ {x},{y}"
        });
    }
}
