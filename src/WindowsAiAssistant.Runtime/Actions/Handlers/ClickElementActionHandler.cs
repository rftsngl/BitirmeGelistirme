using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class ClickElementActionHandler : IActionHandler
{
    private readonly UiAutomationService _uiAutomation;

    public ClickElementActionHandler(UiAutomationService uiAutomation) =>
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

    public string ActionName => "click_element";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return new ActionResult { Success = false, Message = "click_element icin target veya parameters.elementId gerekli." };
        }

        try
        {
            var detail = await _uiAutomation.ClickElementAsync(elementId, cancellationToken).ConfigureAwait(false);
            return new ActionResult { Success = true, Message = $"Element tiklandi: {detail}" };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
