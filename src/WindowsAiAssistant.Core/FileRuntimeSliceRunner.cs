using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core;

public sealed class FileRuntimeSliceRunner : IFileRuntimeSliceRunner, IRuntimeSlice
{
    public const string SliceName = "file";
    public const int SlicePriority = 10;

    private readonly FileStepRuntimeEngagementBoundary _engagementBoundary;
    private readonly FileStepEntryDecider _stepEntryDecider;
    private readonly FileStepFollowUpDecider _stepFollowUpDecider;
    private readonly StepRuntimeCoordinator _stepRuntimeCoordinator;

    public FileRuntimeSliceRunner(
        FileStepRuntimeEngagementBoundary engagementBoundary,
        FileStepEntryDecider stepEntryDecider,
        FileStepFollowUpDecider stepFollowUpDecider,
        StepRuntimeCoordinator stepRuntimeCoordinator)
    {
        _engagementBoundary = engagementBoundary ?? throw new ArgumentNullException(nameof(engagementBoundary));
        _stepEntryDecider = stepEntryDecider ?? throw new ArgumentNullException(nameof(stepEntryDecider));
        _stepFollowUpDecider = stepFollowUpDecider ?? throw new ArgumentNullException(nameof(stepFollowUpDecider));
        _stepRuntimeCoordinator = stepRuntimeCoordinator ?? throw new ArgumentNullException(nameof(stepRuntimeCoordinator));
    }

    public string Name => SliceName;
    public int Priority => SlicePriority;

    public async Task<FileRuntimeSliceResult> TryRunAsync(
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
            return new FileRuntimeSliceResult
            {
                EngagementDecision = engagementDecision,
                InitialState = stepState
            };
        }

        var initialDecision = _stepEntryDecider.DecideInitialStep(stepState);
        var outcome = await _stepRuntimeCoordinator.ExecutePathAsync(stepState, initialDecision, _stepFollowUpDecider, cancellationToken);

        return new FileRuntimeSliceResult
        {
            EngagementDecision = engagementDecision,
            InitialState = stepState,
            InitialDecision = initialDecision,
            Outcome = outcome
        };
    }

    async Task<IRuntimeSliceResult> IRuntimeSlice.TryRunAsync(
        AgentExecutionContext executionContext,
        AgentAction proposedAction,
        CancellationToken cancellationToken)
    {
        return await TryRunAsync(executionContext, proposedAction, cancellationToken);
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
