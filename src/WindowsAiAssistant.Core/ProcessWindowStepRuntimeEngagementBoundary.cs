using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class ProcessWindowStepRuntimeEngagementBoundary : IStepRuntimeEngagementBoundary
{
    private const string ProcessVerificationCapabilityName = "ProcessVerificationCapability";
    private const string VerifyProcessRunningActionName = "VerifyProcessRunning";

    public StepRuntimeEngagementDecision EvaluateEngagement(AgentStepState state, AgentAction proposedAction)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(proposedAction);

        if (!proposedAction.CapabilityName.Equals(ProcessVerificationCapabilityName, StringComparison.OrdinalIgnoreCase) ||
            !proposedAction.ActionName.Equals(VerifyProcessRunningActionName, StringComparison.OrdinalIgnoreCase))
        {
            return Decline("Narrow process/window runtime slice declined: proposed action is outside the runtime slice entry path.");
        }

        if (proposedAction.Target?.Kind is not (TargetKind.Process or TargetKind.Application))
        {
            return Decline("Narrow process/window runtime slice declined: process/application runtime target is required.");
        }

        var hasRuntimeTarget = state.ExecutionContext.ResolvedTargets.Any(target =>
            target.Kind is TargetKind.Process or TargetKind.Application);

        if (!hasRuntimeTarget)
        {
            var grounding = state.ExecutionContext.PrimaryTargetGrounding;
            hasRuntimeTarget = grounding is not null &&
                               grounding.Disposition == TargetGroundingDisposition.Resolved &&
                               grounding.Target.Kind == GroundedTargetKind.KnownApplication &&
                               grounding.ExecutionSuitability == TargetExecutionSuitability.ExecutableHere &&
                               !string.IsNullOrWhiteSpace(grounding.Target.CanonicalValue);
        }

        if (!hasRuntimeTarget)
        {
            return Decline("Narrow process/window runtime slice declined: grounded runtime target is unavailable.");
        }

        var observation = state.ExecutionContext.Observation;
        if (!ObservationSliceSelectionPolicy.IsOperationallyUsable(observation, out var operationalReason))
        {
            return Decline($"Narrow process/window runtime slice declined: observation is not operationally usable ({operationalReason}).");
        }

        var isCurrentContextIntent = ObservationSliceSelectionPolicy.IsCurrentContextIntent(state.ExecutionContext.RawInput);
        if (isCurrentContextIntent &&
            string.IsNullOrWhiteSpace(observation?.ActiveWindow?.Title) &&
            observation?.ActiveWindow?.Handle is not long)
        {
            return Decline("Narrow process/window runtime slice declined: current-window intent requires anchored foreground window observation.");
        }

        if (ObservationSliceSelectionPolicy.IsStrongTargetConflict(proposedAction.Target, observation))
        {
            return Decline("Narrow process/window runtime slice declined: proposed target conflicts with foreground observation context.");
        }

        return new StepRuntimeEngagementDecision
        {
            Disposition = StepRuntimeEngagementDisposition.Engage,
            Message = "Narrow process/window runtime slice engaged."
        };
    }

    private static StepRuntimeEngagementDecision Decline(string message)
    {
        return new StepRuntimeEngagementDecision
        {
            Disposition = StepRuntimeEngagementDisposition.Decline,
            Message = message
        };
    }
}
