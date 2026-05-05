using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core;

internal sealed class ContextProvenanceConsistencyResult
{
    public bool IsConsistent { get; init; }
    public string Source { get; init; } = "live";
}

internal static class ContextProvenanceConsistencyHelper
{
    public static ContextProvenanceConsistencyResult Evaluate(
        AgentExecutionContext? context,
        IReadOnlyDictionary<string, string> metadata)
    {
        var source = ResolveSource(metadata);

        var hasContextAdapter = context?.ContextAdapter is not null;
        var hasObservation = context?.Observation is not null;

        var consistent = true;

        if (TryReadBoolean(metadata, "adapterContextUsed", out var adapterUsedMetadata))
        {
            if (adapterUsedMetadata != hasContextAdapter)
            {
                consistent = false;
            }
        }

        if (TryReadBoolean(metadata, "approval_snapshot_context_used", out var snapshotContextUsed))
        {
            var expectedContextUsed = hasContextAdapter || hasObservation;
            if (snapshotContextUsed != expectedContextUsed)
            {
                consistent = false;
            }
        }

        if (TryReadBoolean(metadata, "approval_snapshot_adapter_preserved", out var snapshotAdapterPreserved))
        {
            if (snapshotAdapterPreserved != hasContextAdapter)
            {
                consistent = false;
            }
        }

        if (TryReadBoolean(metadata, "approval_snapshot_observation_preserved", out var snapshotObservationPreserved))
        {
            if (snapshotObservationPreserved != hasObservation)
            {
                consistent = false;
            }
        }

        if (context is not null && context.ResolvedTargets.Count > 0)
        {
            if (context.ResolvedTargets.Any(t => t.ResolutionReasonKind == TargetResolutionReasonKind.AdapterContext) &&
                !hasContextAdapter)
            {
                consistent = false;
            }

            if (metadata.TryGetValue("target_resolution_reason", out var reasonText) &&
                !context.ResolvedTargets.Any(t =>
                    t.ResolutionReasonKind.ToString().Equals(reasonText, StringComparison.OrdinalIgnoreCase)))
            {
                consistent = false;
            }
        }

        if (metadata.TryGetValue("adapterContext", out var adapterContextName))
        {
            if (!hasContextAdapter ||
                string.IsNullOrWhiteSpace(adapterContextName) ||
                !adapterContextName.Equals(context!.ContextAdapter!.AdapterName, StringComparison.OrdinalIgnoreCase))
            {
                consistent = false;
            }
        }

        return new ContextProvenanceConsistencyResult
        {
            IsConsistent = consistent,
            Source = source
        };
    }

    private static string ResolveSource(IReadOnlyDictionary<string, string> metadata)
    {
        if (TryReadBoolean(metadata, "approvalSnapshotUsed", out var approvalSnapshotUsed) && approvalSnapshotUsed)
        {
            return "snapshot";
        }

        return "live";
    }

    private static bool TryReadBoolean(IReadOnlyDictionary<string, string> metadata, string key, out bool value)
    {
        value = false;
        return metadata.TryGetValue(key, out var rawValue) && bool.TryParse(rawValue, out value);
    }
}