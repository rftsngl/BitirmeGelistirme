using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class ExpandCollapseActionHandler : IActionHandler
{
    private readonly UiAutomationService _uiAutomation;

    public ExpandCollapseActionHandler(UiAutomationService uiAutomation) =>
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

    public string ActionName => "expand_collapse";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var elementId = ActionParameterReader.GetTargetOrParameter(action, "elementId");
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return new ActionResult { Success = false, Message = "expand_collapse icin target veya parameters.elementId gerekli." };
        }

        var mode = action.Parameters.TryGetValue("mode", out var modeValue) ? modeValue : null;

        try
        {
            var detail = await _uiAutomation.ExpandCollapseElementAsync(elementId, mode, cancellationToken).ConfigureAwait(false);
            return new ActionResult { Success = true, Message = detail };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = ex.Message };
        }
    }
}
