using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class ScrollActionHandler : IActionHandler
{
    private readonly UiAutomationService _uiAutomation;

    public ScrollActionHandler(UiAutomationService uiAutomation) =>
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

    public string ActionName => "scroll";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return new ActionResult { Success = false, Message = "scroll icin target veya parameters.elementId gerekli." };
        }

        var direction = action.Parameters.TryGetValue("direction", out var dir) && !string.IsNullOrWhiteSpace(dir)
            ? dir
            : "down";

        try
        {
            var detail = await _uiAutomation.ScrollElementAsync(elementId, direction, cancellationToken).ConfigureAwait(false);
            return new ActionResult { Success = true, Message = detail };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
