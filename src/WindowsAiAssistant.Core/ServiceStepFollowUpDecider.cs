using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class ServiceStepFollowUpDecider : IStepFollowUpDecider
{
    private const string ServiceStatusCapabilityName = "ServiceStatusCapability";
    private const string VerifyServiceStatusActionName = "VerifyServiceStatus";
    private const string ServiceControlCapabilityName = "ServiceControlCapability";
    private const string StartServiceActionName = "StartService";
    private const string StopServiceActionName = "StopService";

    private readonly ICapabilityRegistry _capabilityRegistry;

    public ServiceStepFollowUpDecider(ICapabilityRegistry capabilityRegistry)
    {
        _capabilityRegistry = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));
    }

    public StepDecision DecideNextStep(AgentStepState state, StepFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(feedback);

        if (feedback.ExecutedAction.CapabilityName.Equals(ServiceStatusCapabilityName, StringComparison.OrdinalIgnoreCase) &&
            feedback.ExecutedAction.ActionName.Equals(VerifyServiceStatusActionName, StringComparison.OrdinalIgnoreCase))
        {
            return DecideAfterStatus(state, feedback);
        }

        if (feedback.ExecutedAction.CapabilityName.Equals(ServiceControlCapabilityName, StringComparison.OrdinalIgnoreCase) &&
            (feedback.ExecutedAction.ActionName.Equals(StartServiceActionName, StringComparison.OrdinalIgnoreCase) ||
             feedback.ExecutedAction.ActionName.Equals(StopServiceActionName, StringComparison.OrdinalIgnoreCase)))
        {
            return DecideAfterControl(state, feedback);
        }

        return Stop(feedback.ExecutionResult.Message);
    }

    private StepDecision DecideAfterStatus(AgentStepState state, StepFeedback feedback)
    {
        if (!TryGetServiceExistsState(feedback.ExecutionResult, out var serviceExists) || !serviceExists)
        {
            return Stop(feedback.ExecutionResult.Message);
        }

        if (!TryGetServiceRunningState(feedback.ExecutionResult, out var serviceRunning))
        {
            return Stop(feedback.ExecutionResult.Message);
        }

        var desiredRunning = ResolveDesiredRunning(state);
        if (!desiredRunning.HasValue || desiredRunning.Value == serviceRunning)
        {
            return Stop(feedback.ExecutionResult.Message);
        }

        if (_capabilityRegistry.FindByName(ServiceControlCapabilityName) is null)
        {
            return Stop("Service verification completed, but control capability is unavailable for follow-up.");
        }

        if (feedback.ExecutedAction.Target is null)
        {
            return Stop("Service verification completed, but reusable service target is unavailable for follow-up.");
        }

        var controlActionName = desiredRunning.Value ? StartServiceActionName : StopServiceActionName;
        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Continue,
            NextAction = BuildControlAction(feedback.ExecutedAction.Target, controlActionName),
            Message = desiredRunning.Value
                ? "Service verification completed: service is not running, continuing with start action."
                : "Service verification completed: service is running, continuing with stop action."
        };
    }

    private StepDecision DecideAfterControl(AgentStepState state, StepFeedback feedback)
    {
        if (feedback.ExecutionResult.Status != ExecutionStatus.Succeeded)
        {
            return Stop(feedback.ExecutionResult.Message);
        }

        if (_capabilityRegistry.FindByName(ServiceStatusCapabilityName) is null)
        {
            return Stop("Service control completed, but status capability is unavailable for re-verification.");
        }

        if (feedback.ExecutedAction.Target is null)
        {
            return Stop("Service control completed, but reusable service target is unavailable for re-verification.");
        }

        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Continue,
            NextAction = BuildVerifyAction(feedback.ExecutedAction.Target),
            Message = "Service control completed: re-verifying service status."
        };
    }

    private static bool? ResolveDesiredRunning(AgentStepState state)
    {
        return state.ExecutionContext.DetectedIntent switch
        {
            CommandIntentKind.StartService => true,
            CommandIntentKind.StopService => false,
            _ => null
        };
    }

    private static bool TryGetServiceExistsState(ActionExecutionResult executionResult, out bool serviceExists)
    {
        serviceExists = false;

        return executionResult.OutputData is not null &&
               executionResult.OutputData.TryGetValue("serviceExists", out var value) &&
               bool.TryParse(value, out serviceExists);
    }

    private static bool TryGetServiceRunningState(ActionExecutionResult executionResult, out bool serviceRunning)
    {
        serviceRunning = false;

        return executionResult.OutputData is not null &&
               executionResult.OutputData.TryGetValue("serviceRunning", out var value) &&
               bool.TryParse(value, out serviceRunning);
    }

    private static AgentAction BuildControlAction(TargetReference target, string actionName)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["service"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = ServiceControlCapabilityName,
            ActionName = actionName,
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = $"Follow-up service {actionName.ToLowerInvariant()} step after status verification."
        };
    }

    private static AgentAction BuildVerifyAction(TargetReference target)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["service"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = ServiceStatusCapabilityName,
            ActionName = VerifyServiceStatusActionName,
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Follow-up re-verify step after service control."
        };
    }

    private static StepDecision Stop(string message)
    {
        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Stop,
            NextAction = null,
            Message = message
        };
    }
}
