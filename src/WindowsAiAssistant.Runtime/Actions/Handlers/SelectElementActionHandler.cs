using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class SelectElementActionHandler : IActionHandler
{
    private readonly UiAutomationService _uiAutomation;

    public SelectElementActionHandler(UiAutomationService uiAutomation) =>
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

    public string ActionName => "select_element";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return new ActionResult { Success = false, Message = "select_element icin target veya parameters.elementId gerekli." };
        }

        try
        {
            var detail = await _uiAutomation.SelectElementAsync(elementId, cancellationToken).ConfigureAwait(false);
            return new ActionResult { Success = true, Message = detail };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
