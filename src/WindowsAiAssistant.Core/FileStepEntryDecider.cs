using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class FileStepEntryDecider
{
    private const string FileVerificationCapabilityName = "FileVerificationCapability";
    private const string VerifyFileExistsActionName = "VerifyFileExists";

    public StepDecision DecideInitialStep(AgentStepState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var target = state.ExecutionContext.ResolvedTargets.FirstOrDefault(candidate => candidate.Kind == TargetKind.File);
        if (target is null)
        {
            return new StepDecision
            {
                Disposition = StepContinuationDisposition.Stop,
                NextAction = null,
                Message = "Step runtime entry did not find a file target for the narrow file path."
            };
        }

        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Continue,
            NextAction = BuildVerifyFileAction(target),
            Message = "Start narrow file runtime path with file verification."
        };
    }

    private static AgentAction BuildVerifyFileAction(TargetReference target)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["file"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = FileVerificationCapabilityName,
            ActionName = VerifyFileExistsActionName,
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Initial narrow file runtime step."
        };
    }
}
