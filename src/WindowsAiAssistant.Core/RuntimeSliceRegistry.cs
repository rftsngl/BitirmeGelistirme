using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core;

public sealed class RuntimeSliceRegistry : IRuntimeSliceRegistry
{
    public RuntimeSliceRegistry(IEnumerable<IRuntimeSlice> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);
        Slices = slices
            .OrderBy(slice => slice.Priority)
            .ThenBy(slice => slice.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<IRuntimeSlice> Slices { get; }

    public async Task<RuntimeSliceSelection?> TrySelectAndRunAsync(
        AgentExecutionContext executionContext,
        AgentAction proposedAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(proposedAction);

        foreach (var slice in Slices)
        {
            IRuntimeSliceResult result;
            try
            {
                result = await slice.TryRunAsync(executionContext, proposedAction, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }

            if (result is null)
            {
                continue;
            }

            if (result.EngagementDecision.Disposition == StepRuntimeEngagementDisposition.Engage)
            {
                return new RuntimeSliceSelection
                {
                    SliceName = slice.Name,
                    Result = result
                };
            }
        }

        return null;
    }
}
