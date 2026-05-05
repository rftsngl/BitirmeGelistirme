using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class ProcessWindowStepFollowUpDecider : IStepFollowUpDecider
{
    private const string ProcessVerificationCapabilityName = "ProcessVerificationCapability";
    private const string VerifyProcessRunningActionName = "VerifyProcessRunning";
    private const string WindowProcessCapabilityName = "WindowProcessCapability";
    private const string FocusWindowActionName = "FocusWindow";

    private readonly ICapabilityRegistry _capabilityRegistry;

    public ProcessWindowStepFollowUpDecider(ICapabilityRegistry capabilityRegistry)
    {
        _capabilityRegistry = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));
    }

    public StepDecision DecideNextStep(AgentStepState state, StepFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(feedback);

        if (!feedback.ExecutedAction.CapabilityName.Equals(ProcessVerificationCapabilityName, StringComparison.OrdinalIgnoreCase) ||
            !feedback.ExecutedAction.ActionName.Equals(VerifyProcessRunningActionName, StringComparison.OrdinalIgnoreCase))
        {
            return Stop(feedback.ExecutionResult.Message);
        }

        if (!TryGetProcessRunningState(feedback.ExecutionResult, out var isRunning) || !isRunning)
        {
            return Stop(feedback.ExecutionResult.Message);
        }

        if (feedback.ExecutedAction.Target is null)
        {
            return Stop("Process verification completed, but no reusable target was available for a follow-up step.");
        }

        if (_capabilityRegistry.FindByName(WindowProcessCapabilityName) is null)
        {
            return Stop("Process verification completed, but no focus capability is available for a follow-up step.");
        }

        var effectiveObservation = feedback.RefreshedObservation ?? state.CurrentObservation ?? state.ExecutionContext.Observation;
        if (!TryIsTargetForegroundAligned(feedback.ExecutedAction.Target, effectiveObservation, out var isAligned))
        {
            return Stop("Process verification completed, but refreshed observation did not provide enough state for a follow-up focus step.");
        }

        if (isAligned)
        {
            return Stop("Process verification completed: target process is already foreground-aligned, so no follow-up focus step is needed.");
        }

        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Continue,
            NextAction = BuildFocusWindowFollowUpAction(feedback.ExecutedAction.Target),
            Message = "Process verification completed: target process is running but not foreground-aligned, so a follow-up focus step is required."
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

    private static bool TryGetProcessRunningState(ActionExecutionResult executionResult, out bool isRunning)
    {
        isRunning = false;

        if (executionResult.OutputData is null ||
            !executionResult.OutputData.TryGetValue("isRunning", out var isRunningText))
        {
            return false;
        }

        return bool.TryParse(isRunningText, out isRunning);
    }

    private static bool TryIsTargetForegroundAligned(
        TargetReference target,
        ObservationSnapshot? observation,
        out bool isAligned)
    {
        isAligned = false;

        if (!ObservationTargetAlignment.TryExtractTargetProcessName(target, out var targetProcessName))
        {
            return false;
        }

        if (!ObservationTargetAlignment.TryExtractObservedProcessName(observation, out var foregroundProcessName))
        {
            return false;
        }

        isAligned = targetProcessName.Equals(
            foregroundProcessName,
            StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static AgentAction BuildFocusWindowFollowUpAction(TargetReference target)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["target"] = target.NormalizedValue;
        }

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("processName", out var processName) &&
            !string.IsNullOrWhiteSpace(processName))
        {
            parameters["processName"] = processName;
        }

        if (target.Metadata is not null &&
            target.Metadata.TryGetValue("windowTitle", out var windowTitle) &&
            !string.IsNullOrWhiteSpace(windowTitle))
        {
            parameters["windowTitle"] = windowTitle;
        }

        return new AgentAction
        {
            CapabilityName = WindowProcessCapabilityName,
            ActionName = FocusWindowActionName,
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Feedback-driven follow-up focus step after process verification."
        };
    }

}
