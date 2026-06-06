using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class ReadElementActionHandler : IActionHandler
{
    private readonly UiAutomationService _uiAutomation;

    public ReadElementActionHandler(UiAutomationService uiAutomation) =>
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

    public string ActionName => "read_element";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return new ActionResult { Success = false, Message = "read_element icin target veya parameters.elementId gerekli." };
        }

        try
        {
            var content = await _uiAutomation.ReadElementAsync(elementId, cancellationToken).ConfigureAwait(false);
            return new ActionResult { Success = true, Message = content };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
