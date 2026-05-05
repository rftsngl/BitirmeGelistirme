using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure.ActionPrimitives;

public interface IActionPrimitiveHandler
{
    bool CanHandle(ActionPrimitive primitive);

    Task<ActionPrimitiveExecutionResult> ExecuteAsync(
        ActionPrimitiveExecutionRequest request,
        CancellationToken cancellationToken = default);
}
