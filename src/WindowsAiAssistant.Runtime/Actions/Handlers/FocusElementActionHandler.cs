using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class FocusElementActionHandler : IActionHandler
{
    private readonly UiAutomationService _uiAutomation;

    public FocusElementActionHandler(UiAutomationService uiAutomation) =>
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

    public string ActionName => "focus_element";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return new ActionResult { Success = false, Message = "focus_element icin target veya parameters.elementId gerekli." };
        }

        try
        {
            var detail = await _uiAutomation.FocusElementAsync(elementId, cancellationToken).ConfigureAwait(false);
            return new ActionResult { Success = true, Message = detail };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
