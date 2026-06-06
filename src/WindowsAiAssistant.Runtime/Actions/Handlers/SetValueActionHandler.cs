using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class SetValueActionHandler : IActionHandler
{
    private readonly UiAutomationService _uiAutomation;

    public SetValueActionHandler(UiAutomationService uiAutomation) =>
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

    public string ActionName => "set_value";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        var value = ActionParameterReader.GetTargetOrParameter(action, "value", "text");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return new ActionResult { Success = false, Message = "set_value icin target veya parameters.elementId gerekli." };
        }

        if (value is null)
        {
            return new ActionResult { Success = false, Message = "set_value icin parameters.value gerekli." };
        }

        try
        {
            var detail = await _uiAutomation.SetValueAsync(elementId, value, cancellationToken).ConfigureAwait(false);
            return new ActionResult { Success = true, Message = detail };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
