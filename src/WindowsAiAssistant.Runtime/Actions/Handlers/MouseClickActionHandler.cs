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

        var buttonRaw = ActionParameterReader.GetTargetOrParameter(action, "button");
        if (!MouseButtonParser.TryParse(buttonRaw, out var button))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "mouse_click parameters.button degeri left|right|middle olmali."
            });
        }

        MouseInput.Click(x, y, button);
        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = $"Mouse {button.ToString().ToLowerInvariant()} click @ {x},{y}"
        });
    }
}
