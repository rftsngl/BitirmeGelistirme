using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.Core;

public sealed class ServiceStepEntryDecider
{
    private const string ServiceStatusCapabilityName = "ServiceStatusCapability";
    private const string VerifyServiceStatusActionName = "VerifyServiceStatus";

    public StepDecision DecideInitialStep(AgentStepState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var target = state.ExecutionContext.ResolvedTargets.FirstOrDefault(candidate => candidate.Kind == TargetKind.Service);
        if (target is null)
        {
            return new StepDecision
            {
                Disposition = StepContinuationDisposition.Stop,
                NextAction = null,
                Message = "Step runtime entry did not find a service target for the narrow service path."
            };
        }

        return new StepDecision
        {
            Disposition = StepContinuationDisposition.Continue,
            NextAction = BuildVerifyServiceStatusAction(target),
            Message = "Start narrow service runtime path with service status verification."
        };
    }

    private static AgentAction BuildVerifyServiceStatusAction(TargetReference target)
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
            Notes = "Initial narrow service runtime step."
        };
    }
}
