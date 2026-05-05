using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class ProcessWindowStepEntryDecider : IStepEntryDecider
{
    private const string ProcessVerificationCapabilityName = "ProcessVerificationCapability";
    private const string VerifyProcessRunningActionName = "VerifyProcessRunning";

    public StepDecision DecideInitialStep(AgentStepState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var target = state.ExecutionContext.ResolvedTargets.FirstOrDefault(candidate =>
            candidate.Kind is TargetKind.Process or TargetKind.Application);

        if (target is null)
        {
            target = TryCreateTargetFromGrounding(state.ExecutionContext.PrimaryTargetGrounding);
        }

        if (target is null)
        {
            return new StepDecision
            {
                Disposition = StepContinuationDisposition.Stop,
                NextAction = null,
                Message = "Step runtime entry did not find a process/application target for the narrow process/window path."
            };
        }

        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Continue,
            NextAction = BuildVerifyProcessAction(target),
            Message = "Start narrow process/window runtime path with process verification."
        };
    }

    private static AgentAction BuildVerifyProcessAction(TargetReference target)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            parameters["process"] = target.NormalizedValue;
        }

        return new AgentAction
        {
            CapabilityName = ProcessVerificationCapabilityName,
            ActionName = VerifyProcessRunningActionName,
            Target = target,
            Parameters = parameters,
            RequiresForeground = false,
            RequiresObservation = false,
            Notes = "Initial narrow process/window runtime step."
        };
    }

    private static TargetReference? TryCreateTargetFromGrounding(TargetGroundingResult? grounding)
    {
        if (grounding is null ||
            grounding.Disposition != TargetGroundingDisposition.Resolved ||
            grounding.Target.Kind != GroundedTargetKind.KnownApplication ||
            grounding.ExecutionSuitability != TargetExecutionSuitability.ExecutableHere ||
            string.IsNullOrWhiteSpace(grounding.Target.CanonicalValue))
        {
            return null;
        }

        return new TargetReference
        {
            Kind = TargetKind.Process,
            OriginalText = grounding.Target.OriginalText,
            NormalizedValue = grounding.Target.CanonicalValue,
            DisplayName = grounding.Target.CanonicalValue,
            Confidence = grounding.Target.Confidence,
            ResolutionReasonKind = TargetResolutionReasonKind.Unknown,
            ResolutionSourceText = null,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["groundingDisposition"] = grounding.Disposition.ToString(),
                ["groundedTargetKind"] = grounding.Target.Kind.ToString(),
                ["groundingReason"] = grounding.Reason.ToString(),
                ["groundingExecutionSuitability"] = grounding.ExecutionSuitability.ToString()
            }
        };
    }
}
