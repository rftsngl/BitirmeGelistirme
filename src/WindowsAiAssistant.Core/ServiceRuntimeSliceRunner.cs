using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core;

public sealed class ServiceRuntimeSliceRunner : IRuntimeSlice
{
    public const string SliceName = "service";
    public const int SlicePriority = 30;

    private readonly ServiceStepRuntimeEngagementBoundary _engagementBoundary;
    private readonly ServiceStepEntryDecider _stepEntryDecider;
    private readonly ServiceStepFollowUpDecider _stepFollowUpDecider;
    private readonly StepRuntimeCoordinator _stepRuntimeCoordinator;

    public ServiceRuntimeSliceRunner(
        ServiceStepRuntimeEngagementBoundary engagementBoundary,
        ServiceStepEntryDecider stepEntryDecider,
        ServiceStepFollowUpDecider stepFollowUpDecider,
        StepRuntimeCoordinator stepRuntimeCoordinator)
    {
        _engagementBoundary = engagementBoundary ?? throw new ArgumentNullException(nameof(engagementBoundary));
        _stepEntryDecider = stepEntryDecider ?? throw new ArgumentNullException(nameof(stepEntryDecider));
        _stepFollowUpDecider = stepFollowUpDecider ?? throw new ArgumentNullException(nameof(stepFollowUpDecider));
        _stepRuntimeCoordinator = stepRuntimeCoordinator ?? throw new ArgumentNullException(nameof(stepRuntimeCoordinator));
    }

    public string Name => SliceName;
    public int Priority => SlicePriority;

    public async Task<IRuntimeSliceResult> TryRunAsync(
        AgentExecutionContext executionContext,
        AgentAction proposedAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(proposedAction);

        var stepState = CreateStepState(executionContext);
        var engagementDecision = _engagementBoundary.EvaluateEngagement(stepState, proposedAction);
        if (engagementDecision.Disposition != StepRuntimeEngagementDisposition.Engage)
        {
            return new ServiceRuntimeSliceResult
            {
                EngagementDecision = engagementDecision,
                InitialState = stepState
            };
        }

        var initialDecision = _stepEntryDecider.DecideInitialStep(stepState);
        var outcome = await _stepRuntimeCoordinator.ExecutePathAsync(
            stepState,
            initialDecision,
            _stepFollowUpDecider,
            maxSteps: 3,
            cancellationToken);

        return new ServiceRuntimeSliceResult
        {
            EngagementDecision = engagementDecision,
            InitialState = stepState,
            InitialDecision = initialDecision,
            Outcome = outcome
        };
    }

    private static AgentStepState CreateStepState(AgentExecutionContext executionContext)
    {
        return new AgentStepState
        {
            ExecutionContext = executionContext,
            CurrentObservation = executionContext.Observation,
            LastAction = null,
            LastResult = null,
            StepIndex = 0
        };
    }
}
