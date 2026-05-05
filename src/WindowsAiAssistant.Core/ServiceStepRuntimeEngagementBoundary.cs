using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class ServiceStepRuntimeEngagementBoundary
{
    private const string ServiceStatusCapabilityName = "ServiceStatusCapability";
    private const string VerifyServiceStatusActionName = "VerifyServiceStatus";
    private const string ServiceControlCapabilityName = "ServiceControlCapability";
    private const string StartServiceActionName = "StartService";
    private const string StopServiceActionName = "StopService";

    public StepRuntimeEngagementDecision EvaluateEngagement(AgentStepState state, AgentAction proposedAction)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(proposedAction);

        var isServiceEntryAction =
            (proposedAction.CapabilityName.Equals(ServiceStatusCapabilityName, StringComparison.OrdinalIgnoreCase) &&
             proposedAction.ActionName.Equals(VerifyServiceStatusActionName, StringComparison.OrdinalIgnoreCase)) ||
            (proposedAction.CapabilityName.Equals(ServiceControlCapabilityName, StringComparison.OrdinalIgnoreCase) &&
             (proposedAction.ActionName.Equals(StartServiceActionName, StringComparison.OrdinalIgnoreCase) ||
              proposedAction.ActionName.Equals(StopServiceActionName, StringComparison.OrdinalIgnoreCase)));

        if (!isServiceEntryAction)
        {
            return Decline("Narrow service runtime slice declined: proposed action is outside the service runtime entry path.");
        }

        if (proposedAction.Target?.Kind != TargetKind.Service)
        {
            return Decline("Narrow service runtime slice declined: service runtime target is required.");
        }

        var hasServiceTarget = state.ExecutionContext.ResolvedTargets.Any(target => target.Kind == TargetKind.Service);
        if (!hasServiceTarget)
        {
            return Decline("Narrow service runtime slice declined: resolved service target is unavailable.");
        }

        if (proposedAction.ActionName.Equals(StartServiceActionName, StringComparison.OrdinalIgnoreCase) ||
            proposedAction.ActionName.Equals(StopServiceActionName, StringComparison.OrdinalIgnoreCase))
        {
            var observation = state.ExecutionContext.Observation;
            if (!ObservationSliceSelectionPolicy.IsOperationallyUsable(observation, out var operationalReason))
            {
                return Decline($"Narrow service runtime slice declined: observation is not operationally usable ({operationalReason}).");
            }

            var isCurrentContextIntent = ObservationSliceSelectionPolicy.IsCurrentContextIntent(state.ExecutionContext.RawInput);
            if (isCurrentContextIntent &&
                string.IsNullOrWhiteSpace(observation?.ActiveWindow?.Title) &&
                observation?.ActiveWindow?.Handle is not long)
            {
                return Decline("Narrow service runtime slice declined: current-window intent requires anchored foreground window observation.");
            }
        }

        return new StepRuntimeEngagementDecision
        {
            Disposition = StepRuntimeEngagementDisposition.Engage,
            Message = "Narrow service runtime slice engaged."
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
