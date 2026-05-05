using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IActionPrimitiveExecutor
{
    // Primitive execution is currently consumed from tool implementations as a typed sub-seam.
    Task<ActionPrimitiveExecutionResult> ExecuteAsync(
        ActionPrimitiveExecutionRequest request,
        CancellationToken cancellationToken = default);
}
