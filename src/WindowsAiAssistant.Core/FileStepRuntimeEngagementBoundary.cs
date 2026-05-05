using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class FileStepRuntimeEngagementBoundary
{
    private const string FileVerificationCapabilityName = "FileVerificationCapability";
    private const string VerifyFileExistsActionName = "VerifyFileExists";
    private const string FileOpenCapabilityName = "FileOpenCapability";
    private const string OpenExistingFileActionName = "OpenExistingFile";

    public StepRuntimeEngagementDecision EvaluateEngagement(AgentStepState state, AgentAction proposedAction)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(proposedAction);

        var isFileEntryAction =
            (proposedAction.CapabilityName.Equals(FileVerificationCapabilityName, StringComparison.OrdinalIgnoreCase) &&
             proposedAction.ActionName.Equals(VerifyFileExistsActionName, StringComparison.OrdinalIgnoreCase)) ||
            (proposedAction.CapabilityName.Equals(FileOpenCapabilityName, StringComparison.OrdinalIgnoreCase) &&
             proposedAction.ActionName.Equals(OpenExistingFileActionName, StringComparison.OrdinalIgnoreCase));

        if (!isFileEntryAction)
        {
            return Decline("Narrow file runtime slice declined: proposed action is outside the file runtime entry path.");
        }

        if (proposedAction.Target?.Kind != TargetKind.File)
        {
            return Decline("Narrow file runtime slice declined: file runtime target is required.");
        }

        var hasFileTarget = state.ExecutionContext.ResolvedTargets.Any(target => target.Kind == TargetKind.File);
        if (!hasFileTarget)
        {
            return Decline("Narrow file runtime slice declined: resolved file target is unavailable.");
        }

        if (proposedAction.ActionName.Equals(OpenExistingFileActionName, StringComparison.OrdinalIgnoreCase))
        {
            var observation = state.ExecutionContext.Observation;
            if (!ObservationSliceSelectionPolicy.IsOperationallyUsable(observation, out var operationalReason))
            {
                return Decline($"Narrow file runtime slice declined: observation is not operationally usable ({operationalReason}).");
            }

            var isCurrentContextIntent = ObservationSliceSelectionPolicy.IsCurrentContextIntent(state.ExecutionContext.RawInput);
            if (isCurrentContextIntent &&
                string.IsNullOrWhiteSpace(observation?.ActiveWindow?.Title) &&
                observation?.ActiveWindow?.Handle is not long)
            {
                return Decline("Narrow file runtime slice declined: current-window intent requires anchored foreground window observation.");
            }

            if (ObservationSliceSelectionPolicy.IsStrongTargetConflict(proposedAction.Target, observation))
            {
                return Decline("Narrow file runtime slice declined: proposed target conflicts with foreground observation context.");
            }
        }

        return new StepRuntimeEngagementDecision
        {
            Disposition = StepRuntimeEngagementDisposition.Engage,
            Message = "Narrow file runtime slice engaged."
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
