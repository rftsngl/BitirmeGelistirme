using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core;

public static class DecisionInputBundleBuilder
{
    public static DecisionInputBundle Build(
        AgentExecutionContext? context,
        DecisionInputSource source,
        IReadOnlyDictionary<string, string>? metadata = null,
        DecisionCycleRuntimeState? runtimeCycleState = null)
    {
        var mergedMetadata = MergeMetadata(context?.Metadata, metadata, source);
        var provenance = ContextProvenanceConsistencyHelper.Evaluate(context, mergedMetadata);

        if (context is null)
        {
            return new DecisionInputBundle
            {
                Source = source,
                ContextProvenanceConsistent = provenance.IsConsistent,
                ContextProvenanceSource = provenance.Source,
                RuntimeCycleState = runtimeCycleState
            };
        }

        return new DecisionInputBundle
        {
            CorrelationId = context.CorrelationId,
            RawInput = context.RawInput,
            NormalizedInput = context.NormalizedInput,
            Observation = context.Observation,
            ContextAdapter = context.ContextAdapter,
            ResolvedTargets = context.ResolvedTargets,
            ContextProvenanceConsistent = provenance.IsConsistent,
            ContextProvenanceSource = provenance.Source,
            Source = source,
            RuntimeCycleState = runtimeCycleState
        };
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IDictionary<string, string>? contextMetadata,
        IReadOnlyDictionary<string, string>? metadata,
        DecisionInputSource source)
    {
        if ((contextMetadata is null || contextMetadata.Count == 0) &&
            (metadata is null || metadata.Count == 0) &&
            source == DecisionInputSource.Live)
        {
            return EmptyMetadata.Instance;
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (contextMetadata is not null)
        {
            foreach (var kvp in contextMetadata)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
        }

        if (metadata is not null)
        {
            foreach (var kvp in metadata)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
        }

        if (!merged.ContainsKey("approvalSnapshotUsed"))
        {
            merged["approvalSnapshotUsed"] = source == DecisionInputSource.ApprovedSnapshot ? "true" : "false";
        }

        return merged;
    }

    private static class EmptyMetadata
    {
        public static readonly IReadOnlyDictionary<string, string> Instance =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
