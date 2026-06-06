using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class InvokeToggleActionHandler : IActionHandler
{
    private readonly UiAutomationService _uiAutomation;

    public InvokeToggleActionHandler(UiAutomationService uiAutomation) =>
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

    public string ActionName => "invoke_toggle";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return new ActionResult { Success = false, Message = "invoke_toggle icin target veya parameters.elementId gerekli." };
        }

        try
        {
            var detail = await _uiAutomation.ToggleElementAsync(elementId, cancellationToken).ConfigureAwait(false);
            return new ActionResult { Success = true, Message = detail };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
