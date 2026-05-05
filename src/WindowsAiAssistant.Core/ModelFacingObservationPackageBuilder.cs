using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core;

public static class ModelFacingObservationPackageBuilder
{
    public static ModelFacingObservationPackage Build(
        CommandRequest request,
        AiDecisionInput? aiDecisionInput,
        SafetyDisposition? safetyDisposition = null,
        SafetyRiskLevel? safetyRiskLevel = null,
        bool? approvalPath = null,
        bool? snapshotUsed = null,
        bool? snapshotFallback = null,
        AgentExecutionContext? executionContext = null,
        IReadOnlyDictionary<string, string>? additionalFlags = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var effectiveAiDecisionInput = aiDecisionInput ?? request.AiDecisionInput;

        var commandObservation = BuildCommandObservation(request, effectiveAiDecisionInput);
        var targetObservation = BuildTargetObservation(request, effectiveAiDecisionInput);
        var contextObservation = BuildContextObservation(effectiveAiDecisionInput);
        var lastExecutionObservation = BuildLastExecutionObservation(executionContext);
        var decisionCycleObservation = BuildDecisionCycleObservation(effectiveAiDecisionInput);
        var safetyObservation = BuildSafetyObservation(
            safetyDisposition,
            safetyRiskLevel,
            approvalPath,
            snapshotUsed,
            snapshotFallback);

        return new ModelFacingObservationPackage
        {
            CorrelationId = request.CorrelationId,
            CommandText = request.UserInput,
            Command = commandObservation,
            Target = targetObservation,
            Context = contextObservation,
            LastExecution = lastExecutionObservation,
            DecisionCycle = decisionCycleObservation,
            Safety = safetyObservation,
            IntentSummary = commandObservation.IntentSummary,
            TargetSummary = BuildLegacyTargetSummary(targetObservation),
            ContextSummary = BuildLegacyContextSummary(contextObservation),
            LastExecutionOrStepSummary = BuildLegacyLastExecutionSummary(lastExecutionObservation),
            SafetyOrApprovalContextSummary = BuildLegacySafetySummary(safetyObservation),
            RelevantFlags = BuildRelevantFlags(
                request,
                effectiveAiDecisionInput,
                targetObservation,
                contextObservation,
                lastExecutionObservation,
                decisionCycleObservation,
                safetyObservation,
                additionalFlags)
        };
    }

    public static string? BuildLastExecutionOrStepSummary(AgentExecutionContext? executionContext)
    {
        return BuildLegacyLastExecutionSummary(BuildLastExecutionObservation(executionContext));
    }

    private static ModelCommandObservation BuildCommandObservation(
        CommandRequest request,
        AiDecisionInput? aiDecisionInput)
    {
        var rawInput = NormalizeOptionalText(aiDecisionInput?.RawInput) ?? NormalizeOptionalText(request.UserInput);
        var normalizedInput = NormalizeOptionalText(aiDecisionInput?.NormalizedInput);
        if (string.IsNullOrWhiteSpace(normalizedInput) && !string.IsNullOrWhiteSpace(rawInput))
        {
            normalizedInput = rawInput.ToLowerInvariant();
        }

        return new ModelCommandObservation
        {
            NormalizedInput = normalizedInput,
            IntentSummary = normalizedInput ?? rawInput,
            HasAiDecisionInput = aiDecisionInput is not null
        };
    }

    private static ModelTargetObservation BuildTargetObservation(
        CommandRequest request,
        AiDecisionInput? aiDecisionInput)
    {
        var grounding = request.PrimaryTargetGrounding;
        var presence = DetermineTargetPresence(grounding, aiDecisionInput);

        return new ModelTargetObservation
        {
            Presence = presence,
            GroundingStatus = DetermineGroundingStatus(grounding, presence),
            ResolvedTargetCount = aiDecisionInput?.ResolvedTargetCount ?? 0,
            PrimaryTargetKind = aiDecisionInput?.PrimaryTargetKind ?? MapGroundedTargetKind(grounding?.Target.Kind),
            PrimaryTargetReason = aiDecisionInput?.PrimaryTargetReason,
            GroundedTargetKind = grounding?.Target.Kind,
            CanonicalValue = NormalizeOptionalText(grounding?.Target.CanonicalValue)
        };
    }

    private static ModelTargetPresence DetermineTargetPresence(
        TargetGroundingResult? grounding,
        AiDecisionInput? aiDecisionInput)
    {
        if (grounding is not null)
        {
            return ModelTargetPresence.HasTarget;
        }

        if (aiDecisionInput is null)
        {
            return ModelTargetPresence.Unknown;
        }

        if (aiDecisionInput.PrimaryTargetKind is not null ||
            aiDecisionInput.PrimaryTargetReason is not null ||
            aiDecisionInput.ResolvedTargetCount > 0)
        {
            return ModelTargetPresence.HasTarget;
        }

        return ModelTargetPresence.NoTarget;
    }

    private static ModelTargetGroundingStatus DetermineGroundingStatus(
        TargetGroundingResult? grounding,
        ModelTargetPresence presence)
    {
        if (grounding is null)
        {
            return presence == ModelTargetPresence.Unknown
                ? ModelTargetGroundingStatus.Unknown
                : ModelTargetGroundingStatus.NotEvaluated;
        }

        return grounding.Disposition switch
        {
            TargetGroundingDisposition.Resolved => ModelTargetGroundingStatus.Resolved,
            TargetGroundingDisposition.Unresolved => ModelTargetGroundingStatus.Unresolved,
            TargetGroundingDisposition.Unsupported => ModelTargetGroundingStatus.Unsupported,
            TargetGroundingDisposition.Ambiguous => ModelTargetGroundingStatus.Ambiguous,
            _ => ModelTargetGroundingStatus.Unknown
        };
    }

    private static TargetKind? MapGroundedTargetKind(GroundedTargetKind? groundedTargetKind)
    {
        if (groundedTargetKind is null)
        {
            return null;
        }

        return groundedTargetKind.Value switch
        {
            GroundedTargetKind.KnownApplication => TargetKind.Application,
            GroundedTargetKind.PathLike => TargetKind.Path,
            GroundedTargetKind.DocumentLike => TargetKind.File,
            GroundedTargetKind.UrlLike => TargetKind.Url,
            _ => null
        };
    }

    private static ModelContextObservation BuildContextObservation(AiDecisionInput? aiDecisionInput)
    {
        if (aiDecisionInput is null)
        {
            return new ModelContextObservation
            {
                Availability = ModelSignalAvailability.Unknown,
                DecisionInputSource = null,
                ContextProvenanceSource = null,
                HasAdapterContext = false,
                ContextProvenanceConsistent = false
            };
        }

        var activeProcessName = NormalizeOptionalText(aiDecisionInput.ActiveProcessName);
        var contextAdapterName = NormalizeOptionalText(aiDecisionInput.ContextAdapterName);
        var availability = !string.IsNullOrWhiteSpace(activeProcessName) ||
                           !string.IsNullOrWhiteSpace(contextAdapterName)
            ? ModelSignalAvailability.Available
            : ModelSignalAvailability.NotAvailable;

        return new ModelContextObservation
        {
            Availability = availability,
            ActiveProcessName = activeProcessName,
            ContextAdapterName = contextAdapterName,
            DecisionInputSource = aiDecisionInput.Source,
            ContextProvenanceSource = NormalizeOptionalText(aiDecisionInput.ContextProvenanceSource),
            HasAdapterContext = aiDecisionInput.HasAdapterContext,
            ContextProvenanceConsistent = aiDecisionInput.ContextProvenanceConsistent
        };
    }

    private static ModelLastExecutionObservation BuildLastExecutionObservation(AgentExecutionContext? executionContext)
    {
        if (executionContext is null)
        {
            return new ModelLastExecutionObservation
            {
                RuntimeStateAvailability = ModelSignalAvailability.Unknown,
                VerificationAvailability = ModelSignalAvailability.Unknown,
                Outcome = ModelExecutionOutcome.Unknown
            };
        }

        var runtimeState = executionContext.RichObservation?.RuntimeState;
        if (runtimeState is null)
        {
            return new ModelLastExecutionObservation
            {
                RuntimeStateAvailability = ModelSignalAvailability.NotAvailable,
                VerificationAvailability = ModelSignalAvailability.NotAvailable,
                Outcome = ModelExecutionOutcome.Unknown
            };
        }

        return new ModelLastExecutionObservation
        {
            RuntimeStateAvailability = ModelSignalAvailability.Available,
            Outcome = DetermineExecutionOutcome(runtimeState),
            CapabilityName = NormalizeOptionalText(runtimeState.CapabilityName),
            ActionName = NormalizeOptionalText(runtimeState.ActionName),
            ActionStatus = runtimeState.ActionStatus,
            VerificationStatus = runtimeState.VerificationStatus,
            VerificationAvailability = runtimeState.VerificationStatus.HasValue
                ? ModelSignalAvailability.Available
                : ModelSignalAvailability.NotAvailable,
            BlockedReason = NormalizeOptionalText(runtimeState.BlockedReason),
            FailureReason = NormalizeOptionalText(runtimeState.FailureReason),
            PrimitiveSucceeded = runtimeState.PrimitiveSucceeded
        };
    }

    private static ModelDecisionCycleObservation BuildDecisionCycleObservation(AiDecisionInput? aiDecisionInput)
    {
        if (aiDecisionInput is null ||
            string.IsNullOrWhiteSpace(aiDecisionInput.RuntimeId) &&
            aiDecisionInput.CurrentStepIndex <= 0 &&
            aiDecisionInput.MaxStepLimit <= 0 &&
            aiDecisionInput.PreviousDecisionKind is null &&
            aiDecisionInput.LastExecutionFeedback is null)
        {
            return new ModelDecisionCycleObservation
            {
                Availability = ModelSignalAvailability.NotAvailable,
                GoalStillActive = false
            };
        }

        return new ModelDecisionCycleObservation
        {
            Availability = ModelSignalAvailability.Available,
            RuntimeId = NormalizeOptionalText(aiDecisionInput.RuntimeId),
            CurrentStepIndex = aiDecisionInput.CurrentStepIndex,
            MaxStepLimit = aiDecisionInput.MaxStepLimit,
            PreviousDecisionKind = aiDecisionInput.PreviousDecisionKind,
            PreviousActionType = aiDecisionInput.PreviousActionType,
            PreviousDecisionExecuted = aiDecisionInput.PreviousDecisionExecuted,
            PreviousExecutionOutcome = aiDecisionInput.PreviousExecutionOutcome,
            GoalStillActive = aiDecisionInput.GoalStillActive,
            StepHistorySummary = NormalizeOptionalText(aiDecisionInput.StepHistorySummary),
            RuntimeTerminalState = aiDecisionInput.RuntimeTerminalState,
            LastExecutionFeedback = aiDecisionInput.LastExecutionFeedback
        };
    }

    private static ModelExecutionOutcome DetermineExecutionOutcome(ExecutionRuntimeState runtimeState)
    {
        if (runtimeState.ActionStatus is null)
        {
            if (!string.IsNullOrWhiteSpace(runtimeState.BlockedReason))
            {
                return ModelExecutionOutcome.Blocked;
            }

            if (!string.IsNullOrWhiteSpace(runtimeState.FailureReason))
            {
                return ModelExecutionOutcome.Failed;
            }

            return ModelExecutionOutcome.Unknown;
        }

        return runtimeState.ActionStatus.Value switch
        {
            ExecutionStatus.Planned => ModelExecutionOutcome.NotExecuted,
            ExecutionStatus.Skipped => ModelExecutionOutcome.NotExecuted,
            ExecutionStatus.Attempted => ModelExecutionOutcome.InProgress,
            ExecutionStatus.Succeeded => ModelExecutionOutcome.Completed,
            ExecutionStatus.PartiallySucceeded => ModelExecutionOutcome.Completed,
            ExecutionStatus.Blocked => ModelExecutionOutcome.Blocked,
            ExecutionStatus.Failed => ModelExecutionOutcome.Failed,
            ExecutionStatus.VerificationFailed => ModelExecutionOutcome.Failed,
            _ => ModelExecutionOutcome.Unknown
        };
    }

    private static ModelSafetyObservation BuildSafetyObservation(
        SafetyDisposition? safetyDisposition,
        SafetyRiskLevel? safetyRiskLevel,
        bool? approvalPath,
        bool? snapshotUsed,
        bool? snapshotFallback)
    {
        var hasAnySafetySignal = safetyDisposition.HasValue ||
                                 safetyRiskLevel.HasValue ||
                                 approvalPath.HasValue ||
                                 snapshotUsed.HasValue ||
                                 snapshotFallback.HasValue;

        if (!hasAnySafetySignal)
        {
            return new ModelSafetyObservation
            {
                Availability = ModelSignalAvailability.Unknown,
                ApprovalRequired = null,
                ApprovalPath = null
            };
        }

        var availability = safetyDisposition.HasValue && safetyRiskLevel.HasValue && approvalPath.HasValue
            ? ModelSignalAvailability.Available
            : ModelSignalAvailability.Partial;

        return new ModelSafetyObservation
        {
            Availability = availability,
            Disposition = safetyDisposition,
            RiskLevel = safetyRiskLevel,
            ApprovalRequired = safetyDisposition.HasValue
                ? safetyDisposition.Value == SafetyDisposition.RequiresApproval
                : null,
            ApprovalPath = approvalPath,
            SnapshotUsed = snapshotUsed,
            SnapshotFallback = snapshotFallback
        };
    }

    private static string? BuildLegacyTargetSummary(ModelTargetObservation target)
    {
        if (target.Presence == ModelTargetPresence.Unknown)
        {
            return null;
        }

        if (target.Presence == ModelTargetPresence.NoTarget)
        {
            return "target:none";
        }

        var parts = new List<string>(7)
        {
            $"presence:{target.Presence}",
            $"grounding:{target.GroundingStatus}",
            $"resolvedTargetCount:{target.ResolvedTargetCount}"
        };

        if (target.PrimaryTargetKind is not null)
        {
            parts.Add($"kind:{target.PrimaryTargetKind}");
        }

        if (target.PrimaryTargetReason is not null)
        {
            parts.Add($"reason:{target.PrimaryTargetReason}");
        }

        if (target.GroundedTargetKind is not null)
        {
            parts.Add($"groundedKind:{target.GroundedTargetKind}");
        }

        if (!string.IsNullOrWhiteSpace(target.CanonicalValue))
        {
            parts.Add($"value:{target.CanonicalValue}");
        }

        return string.Join("; ", parts);
    }

    private static string? BuildLegacyContextSummary(ModelContextObservation context)
    {
        if (context.Availability == ModelSignalAvailability.Unknown)
        {
            return null;
        }

        var parts = new List<string>(5)
        {
            $"availability:{context.Availability}"
        };

        if (context.DecisionInputSource is not null)
        {
            parts.Add($"source:{context.DecisionInputSource.Value.ToString().ToLowerInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(context.ContextProvenanceSource))
        {
            parts.Add($"provenance:{context.ContextProvenanceSource}");
        }

        if (!string.IsNullOrWhiteSpace(context.ActiveProcessName))
        {
            parts.Add($"activeProcess:{context.ActiveProcessName}");
        }

        if (!string.IsNullOrWhiteSpace(context.ContextAdapterName))
        {
            parts.Add($"contextAdapter:{context.ContextAdapterName}");
        }

        return string.Join("; ", parts);
    }

    private static string? BuildLegacyLastExecutionSummary(ModelLastExecutionObservation lastExecution)
    {
        if (lastExecution.RuntimeStateAvailability == ModelSignalAvailability.Unknown)
        {
            return null;
        }

        if (lastExecution.RuntimeStateAvailability == ModelSignalAvailability.NotAvailable)
        {
            return "runtime:not-available";
        }

        var parts = new List<string>(9)
        {
            $"runtime:{lastExecution.RuntimeStateAvailability}",
            $"outcome:{lastExecution.Outcome}"
        };

        if (!string.IsNullOrWhiteSpace(lastExecution.CapabilityName))
        {
            parts.Add($"capability:{lastExecution.CapabilityName}");
        }

        if (!string.IsNullOrWhiteSpace(lastExecution.ActionName))
        {
            parts.Add($"action:{lastExecution.ActionName}");
        }

        if (lastExecution.ActionStatus is not null)
        {
            parts.Add($"actionStatus:{lastExecution.ActionStatus}");
        }

        if (lastExecution.VerificationStatus is not null)
        {
            parts.Add($"verification:{lastExecution.VerificationStatus}");
        }

        if (!string.IsNullOrWhiteSpace(lastExecution.BlockedReason))
        {
            parts.Add($"blockedReason:{lastExecution.BlockedReason}");
        }

        if (!string.IsNullOrWhiteSpace(lastExecution.FailureReason))
        {
            parts.Add($"failureReason:{lastExecution.FailureReason}");
        }

        if (lastExecution.PrimitiveSucceeded.HasValue)
        {
            parts.Add($"primitiveSucceeded:{(lastExecution.PrimitiveSucceeded.Value ? "true" : "false")}");
        }

        return string.Join("; ", parts);
    }

    private static string? BuildLegacySafetySummary(ModelSafetyObservation safety)
    {
        if (safety.Availability == ModelSignalAvailability.Unknown)
        {
            return null;
        }

        var parts = new List<string>(7)
        {
            $"availability:{safety.Availability}"
        };

        if (safety.Disposition is not null)
        {
            parts.Add($"safety:{safety.Disposition}");
        }

        if (safety.RiskLevel is not null)
        {
            parts.Add($"risk:{safety.RiskLevel}");
        }

        if (safety.ApprovalRequired.HasValue)
        {
            parts.Add($"approvalRequired:{(safety.ApprovalRequired.Value ? "true" : "false")}");
        }

        if (safety.ApprovalPath.HasValue)
        {
            parts.Add($"approvalPath:{(safety.ApprovalPath.Value ? "true" : "false")}");
        }

        if (safety.SnapshotUsed.HasValue)
        {
            parts.Add($"snapshotUsed:{(safety.SnapshotUsed.Value ? "true" : "false")}");
        }

        if (safety.SnapshotFallback.HasValue)
        {
            parts.Add($"snapshotFallback:{(safety.SnapshotFallback.Value ? "true" : "false")}");
        }

        return string.Join("; ", parts);
    }

    private static IReadOnlyDictionary<string, string> BuildRelevantFlags(
        CommandRequest request,
        AiDecisionInput? aiDecisionInput,
        ModelTargetObservation target,
        ModelContextObservation context,
        ModelLastExecutionObservation lastExecution,
        ModelDecisionCycleObservation decisionCycle,
        ModelSafetyObservation safety,
        IReadOnlyDictionary<string, string>? additionalFlags)
    {
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hasPrimaryTargetGrounding"] = request.PrimaryTargetGrounding is null ? "false" : "true",
            ["hasTarget"] = target.Presence == ModelTargetPresence.HasTarget ? "true" : "false",
            ["targetPresence"] = target.Presence.ToString().ToLowerInvariant(),
            ["targetGroundingStatus"] = target.GroundingStatus.ToString().ToLowerInvariant(),
            ["contextAvailability"] = context.Availability.ToString().ToLowerInvariant(),
            ["runtimeStateAvailability"] = lastExecution.RuntimeStateAvailability.ToString().ToLowerInvariant(),
            ["runtimeOutcome"] = lastExecution.Outcome.ToString().ToLowerInvariant(),
            ["decisionCycleAvailability"] = decisionCycle.Availability.ToString().ToLowerInvariant(),
            ["verificationAvailability"] = lastExecution.VerificationAvailability.ToString().ToLowerInvariant(),
            ["safetyAvailability"] = safety.Availability.ToString().ToLowerInvariant()
        };

        if (aiDecisionInput is not null)
        {
            flags["hasContextualTarget"] = aiDecisionInput.HasContextualTarget ? "true" : "false";
            flags["hasAdapterContext"] = aiDecisionInput.HasAdapterContext ? "true" : "false";
            flags["contextProvenanceConsistent"] = aiDecisionInput.ContextProvenanceConsistent ? "true" : "false";
            flags["decisionInputSource"] = aiDecisionInput.Source.ToString().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(decisionCycle.RuntimeId))
        {
            flags["runtimeId"] = decisionCycle.RuntimeId;
        }

        if (decisionCycle.CurrentStepIndex > 0)
        {
            flags["runtimeStepIndex"] = decisionCycle.CurrentStepIndex.ToString();
        }

        if (decisionCycle.MaxStepLimit > 0)
        {
            flags["runtimeStepLimit"] = decisionCycle.MaxStepLimit.ToString();
        }

        if (decisionCycle.PreviousDecisionKind is not null)
        {
            flags["previousDecisionKind"] = decisionCycle.PreviousDecisionKind.Value.ToString();
        }

        if (decisionCycle.PreviousExecutionOutcome is not null)
        {
            flags["previousExecutionOutcome"] = decisionCycle.PreviousExecutionOutcome.Value.ToString().ToLowerInvariant();
        }

        flags["goalStillActive"] = decisionCycle.GoalStillActive ? "true" : "false";

        if (safety.ApprovalRequired.HasValue)
        {
            flags["approvalRequired"] = safety.ApprovalRequired.Value ? "true" : "false";
        }

        if (safety.ApprovalPath.HasValue)
        {
            flags["approvalPath"] = safety.ApprovalPath.Value ? "true" : "false";
        }

        if (additionalFlags is not null)
        {
            foreach (var kvp in additionalFlags)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    flags[kvp.Key] = kvp.Value;
                }
            }
        }

        return flags;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
