using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Contracts.Models.Verification;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Core;

public sealed class AssistantOrchestrator
{
    private const int DefaultDecisionLoopMaxSteps = 4;
    private const int DefaultDecisionRetryLimit = 2;
    private const string ChainContinueMetadataKey = "chain_continue";
    private const string GoalPendingMetadataKey = "goal_pending";

    private readonly IAiDecisionClient _aiDecisionClient;
    private readonly IAuditLogger _auditLogger;
    private readonly ICapabilityRegistry? _capabilityRegistry;
    private readonly ICommandObservationCollector? _commandObservationCollector;
    private readonly IContextAdapterResolver? _contextAdapterResolver;
    private readonly IExecutionVerifier? _executionVerifier;
    private readonly ICapabilityInvocationRouterRegistry? _capabilityInvocationRouterRegistry;
    private readonly IObservationProvider _observationProvider;
    private readonly IRuntimeSliceRegistry? _runtimeSliceRegistry;
    private readonly RuntimeSliceFallbackPolicy _runtimeSliceFallbackPolicy;
        private readonly ISafetyGate _safetyGate;
    private readonly IStepSafetyEvaluator? _stepSafetyEvaluator;
    private readonly ITargetGrounder? _targetGrounder;
    private readonly ITargetResolver _targetResolver;
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly Dictionary<string, string> _verificationOutcomeByCorrelationId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _submittedStepEventByCorrelationId = new(StringComparer.OrdinalIgnoreCase);

    public void ClearTransientSessionState(string? correlationId = null)
    {
        lock (_verificationOutcomeByCorrelationId)
        {
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                _verificationOutcomeByCorrelationId.Clear();
                return;
            }

            _verificationOutcomeByCorrelationId.Remove(correlationId);
            _verificationOutcomeByCorrelationId.Remove(BuildCacheKeyFromRequestCorrelationId(correlationId));
        }

        lock (_submittedStepEventByCorrelationId)
        {
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                _submittedStepEventByCorrelationId.Clear();
                return;
            }

            _submittedStepEventByCorrelationId.Remove(correlationId);
            _submittedStepEventByCorrelationId.Remove(BuildCacheKeyFromRequestCorrelationId(correlationId));
        }
    }

    public AssistantOrchestrator(
        IAiDecisionClient aiDecisionClient,
        ISafetyGate safetyGate,
        IEnumerable<ITool> tools,
        IAuditLogger auditLogger,
        IObservationProvider observationProvider,
        ITargetResolver targetResolver,
        ICapabilityRegistry? capabilityRegistry = null,
        IContextAdapterResolver? contextAdapterResolver = null,
        IExecutionVerifier? executionVerifier = null,
        ITargetGrounder? targetGrounder = null,
        ICommandObservationCollector? commandObservationCollector = null,
        IStepSafetyEvaluator? stepSafetyEvaluator = null,
        IRuntimeSliceRegistry? runtimeSliceRegistry = null,
        RuntimeSliceFallbackPolicy runtimeSliceFallbackPolicy = RuntimeSliceFallbackPolicy.ControlledFallback,
        ICapabilityInvocationRouterRegistry? capabilityInvocationRouterRegistry = null)
    {
        _aiDecisionClient = aiDecisionClient;
        _safetyGate = safetyGate;
        _stepSafetyEvaluator = stepSafetyEvaluator;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _auditLogger = auditLogger;
        _observationProvider = observationProvider;
        _targetResolver = targetResolver;
        _capabilityRegistry = capabilityRegistry;
        _contextAdapterResolver = contextAdapterResolver;
        _executionVerifier = executionVerifier;
        _runtimeSliceRegistry = runtimeSliceRegistry;
        _runtimeSliceFallbackPolicy = runtimeSliceFallbackPolicy;
        _capabilityInvocationRouterRegistry = capabilityInvocationRouterRegistry
            ?? new Routing.CapabilityInvocationRouterRegistry([new Routing.DefaultCapabilityInvocationRouter()]);
        _targetGrounder = targetGrounder;
        _commandObservationCollector = commandObservationCollector;
    }

    public async Task<CommandResult> HandleAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UserInput))
            {
                var emptyResult = new CommandResult
                {
                    Status = "Denied",
                    Safety = SafetyDisposition.Denied.ToString(),
                    RiskLevel = SafetyRiskLevel.High.ToString(),
                    Decision = "No decision",
                    SelectedTool = "None",
                    ToolExecutionResult = "Not executed",
                    Message = "Command cannot be empty."
                };

                await LogEventAsync(request, "ExecutionBlocked", emptyResult.Safety, emptyResult.RiskLevel, emptyResult.SelectedTool, "not-executed", emptyResult.Status, emptyResult.Message, string.Empty, cancellationToken);
                await LogEventAsync(request, "CommandCompleted", emptyResult.Safety, emptyResult.RiskLevel, emptyResult.SelectedTool, "not-executed", emptyResult.Status, emptyResult.Message, string.Empty, cancellationToken);

                return emptyResult;
            }

            var safetyDecision = await _safetyGate.EvaluateAsync(request, cancellationToken);
            await LogEventAsync(
                request,
                "PolicyEvaluated",
                safetyDecision.Disposition.ToString(),
                safetyDecision.RiskLevel.ToString(),
                "None",
                "not-executed",
                "Evaluated",
                safetyDecision.Reason,
                string.Empty,
                cancellationToken,
                executionContext: null,
                extraMetadata: BuildPolicyEvaluationMetadata("precheck"));

            if (safetyDecision.Disposition == SafetyDisposition.Denied)
            {
                var deniedRuntimeState = CreateInitialDecisionCycleRuntimeState(
                    request,
                    DecisionInputSource.Live,
                    maxStepLimit: 1,
                    maxRetryLimit: DefaultDecisionRetryLimit);

                deniedRuntimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
                deniedRuntimeState.TerminalState = DecisionLoopTerminalState.Aborted;
                deniedRuntimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
                deniedRuntimeState.GoalStillActive = false;
                deniedRuntimeState.Outcome = DecisionRuntimeOutcome.Aborted;
                deniedRuntimeState.TerminationReason = DecisionRuntimeTerminationReason.SafetyDenied;

                AppendDecisionCycleStep(
                    deniedRuntimeState,
                    decisionKind: DecisionKind.Stop,
                    actionType: null,
                    targetSummary: null,
                    outcome: DecisionExecutionOutcome.Blocked,
                    transition: DecisionLoopTransition.Terminal,
                    terminalState: deniedRuntimeState.TerminalState,
                    reason: safetyDecision.Reason);

                await LogStepSubmittedAsync(
                    request,
                    SafetyDisposition.Denied,
                    safetyDecision.RiskLevel,
                    null,
                    null,
                    deniedRuntimeState,
                    selectedTool: "None",
                    executionMode: "not-executed",
                    approvalDecision: string.Empty,
                    cancellationToken,
                    executionPath: "precheck");

                var deniedResult = BuildLoopTerminalResult(
                    request,
                    SafetyDisposition.Denied,
                    safetyDecision.RiskLevel,
                    status: "Denied",
                    selectedTool: "None",
                    decisionText: "Blocked by safety gate",
                    toolExecutionResult: "Not executed",
                    message: safetyDecision.Reason,
                    runtimeState: deniedRuntimeState);

                await LogEventAsync(request, "ExecutionBlocked", deniedResult.Safety, deniedResult.RiskLevel, deniedResult.SelectedTool, "not-executed", deniedResult.Status, deniedResult.Message, string.Empty, cancellationToken);
                await LogEventAsync(request, "CommandCompleted", deniedResult.Safety, deniedResult.RiskLevel, deniedResult.SelectedTool, "not-executed", deniedResult.Status, deniedResult.Message, string.Empty, cancellationToken);

                return deniedResult;
            }

            var precheckRiskLevel = SafetyRiskLevel.Low;

            var executionContext = await BuildExecutionContextAsync(
                request,
                SafetyDisposition.Allowed,
                precheckRiskLevel,
                cancellationToken);
            var runtimeState = CreateInitialDecisionCycleRuntimeState(
                request,
                DecisionInputSource.Live,
                maxStepLimit: DefaultDecisionLoopMaxSteps,
                maxRetryLimit: DefaultDecisionRetryLimit);

            return await RunDecisionLoopAsync(
                request,
                SafetyDisposition.Allowed,
                precheckRiskLevel,
                approvalPath: false,
                executionContext,
                runtimeState,
                decisionInputSource: DecisionInputSource.Live,
                snapshotUsed: null,
                snapshotFallback: null,
                targetReasonNormalized: null,
                targetReasonSource: null,
                snapshotContextUsed: null,
                snapshotAdapterPreserved: null,
                snapshotObservationPreserved: null,
                initialDecision: null,
                approvedStepApprovalKey: null,
                resumePendingStep: false,
                cancellationToken: cancellationToken);
        }
        finally
        {
            lock (_verificationOutcomeByCorrelationId)
            {
                _verificationOutcomeByCorrelationId.Remove(request.CorrelationId);
                _verificationOutcomeByCorrelationId.Remove(BuildCacheKeyFromRequestCorrelationId(request.CorrelationId));
            }
        }
    }

    private async Task<CommandResult> RunDecisionLoopAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        bool approvalPath,
        AgentExecutionContext executionContext,
        DecisionCycleRuntimeState runtimeState,
        DecisionInputSource decisionInputSource,
        bool? snapshotUsed,
        bool? snapshotFallback,
        bool? targetReasonNormalized,
        string? targetReasonSource,
        bool? snapshotContextUsed,
        bool? snapshotAdapterPreserved,
        bool? snapshotObservationPreserved,
        AiDecision? initialDecision,
        string? approvedStepApprovalKey,
        bool resumePendingStep,
        CancellationToken cancellationToken)
    {
        var currentContext = executionContext;
        var currentDecision = initialDecision is null ? null : NormalizeDecision(initialDecision);
        var remainingApprovedStepApprovalKey = approvedStepApprovalKey;
        var isFirstResumedPendingStep = resumePendingStep && currentDecision is not null;

        try
        {
            while (runtimeState.CurrentStepIndex < runtimeState.MaxStepLimit)
            {
            var decisionInputBundle = DecisionInputBundleBuilder.Build(
                currentContext,
                decisionInputSource,
                metadata: null,
                runtimeCycleState: runtimeState);
            var decisionSummary = DecisionSummaryBuilder.Build(decisionInputBundle);
            var aiDecisionInput = AiDecisionInputBuilder.Build(request, decisionSummary);
            var loopFlags = BuildDecisionLoopFlags(runtimeState, currentContext.Metadata);

            var decisionRequest = BuildAiDecisionRequest(
                request,
                aiDecisionInput,
                safetyDisposition: safety,
                safetyRiskLevel: riskLevel,
                approvalPath: approvalPath,
                snapshotUsed: snapshotUsed,
                snapshotFallback: snapshotFallback,
                executionContext: currentContext,
                additionalFlags: loopFlags);

            var decision = currentDecision ?? NormalizeDecision(await _aiDecisionClient.DecideAsync(decisionRequest, cancellationToken));
            currentDecision = null;
            currentContext = ApplyDetectedIntent(currentContext, decision);

            runtimeState.CurrentDecision = decision.NextActionDecision;
            if (isFirstResumedPendingStep)
            {
                UpdateRuntimeSessionCurrentStep(runtimeState, decision.NextActionDecision);
                isFirstResumedPendingStep = false;
            }
            else
            {
                runtimeState.CurrentStepIndex++;
                UpdateRuntimeSessionCurrentStep(runtimeState, decision.NextActionDecision);
            }

            if (decision.NextActionDecision is null)
            {
                runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
                runtimeState.TerminalState = DecisionLoopTerminalState.Completed;
                runtimeState.BlockedState = DecisionRuntimeBlockedState.None;
                runtimeState.GoalStillActive = false;
                runtimeState.Outcome = DecisionRuntimeOutcome.Completed;
                runtimeState.TerminationReason = DecisionRuntimeTerminationReason.GoalCompleted;

                var message = "No matching tool for this command.";
                AppendDecisionCycleStep(
                    runtimeState,
                    decisionKind: DecisionKind.Stop,
                    actionType: null,
                    targetSummary: null,
                    outcome: DecisionExecutionOutcome.NotExecuted,
                    transition: DecisionLoopTransition.Terminal,
                    terminalState: runtimeState.TerminalState,
                    reason: message);

                var failedResult = BuildLoopTerminalResult(
                    request,
                    safety,
                    riskLevel,
                    status: "Accepted",
                    selectedTool: "None",
                    decisionText: string.IsNullOrWhiteSpace(decision.DecisionText)
                        ? message
                        : decision.DecisionText,
                    toolExecutionResult: "Not executed",
                    message,
                    runtimeState);

                await LogDecisionLoopStepAsync(
                    request,
                    safety,
                    riskLevel,
                    currentContext,
                    decision,
                    runtimeState,
                    transition: DecisionLoopTransition.Terminal,
                    message,
                    outcome: "NoAction",
                    selectedTool: "None",
                    executionMode: "not-executed",
                    cancellationToken);

                await LogEventAsync(
                    request,
                    "CommandCompleted",
                    failedResult.Safety,
                    failedResult.RiskLevel,
                    failedResult.SelectedTool,
                    "not-executed",
                    failedResult.Status,
                    failedResult.Message,
                    approvalPath ? "Approved" : string.Empty,
                    cancellationToken,
                    currentContext,
                    BuildDecisionLoopFlags(runtimeState));

                return failedResult;
            }

            var nextAction = decision.NextActionDecision;
            if (nextAction.Kind == DecisionKind.AskApproval)
            {
                if (nextAction.AskApproval?.ProposedAction is null)
                {
                    const string invalidApprovalMessage = "Model requested approval without a proposed action.";

                    runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
                    runtimeState.TerminalState = DecisionLoopTerminalState.Aborted;
                    runtimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
                    runtimeState.GoalStillActive = false;
                    runtimeState.Outcome = DecisionRuntimeOutcome.Aborted;
                    runtimeState.TerminationReason = DecisionRuntimeTerminationReason.SafetyDenied;

                    await LogEventAsync(
                        request,
                        "PolicyEvaluated",
                        SafetyDisposition.Denied.ToString(),
                        SafetyRiskLevel.High.ToString(),
                        "None",
                        "not-executed",
                        "Evaluated",
                        invalidApprovalMessage,
                        approvalPath ? "Approved" : string.Empty,
                        cancellationToken,
                        currentContext,
                        BuildPolicyEvaluationMetadata(
                            "step",
                            approvalAuthorityOutcome: "policy-denied"));

                    AppendDecisionCycleStep(
                        runtimeState,
                        decisionKind: DecisionKind.AskApproval,
                        actionType: null,
                        targetSummary: null,
                        outcome: DecisionExecutionOutcome.Blocked,
                        transition: DecisionLoopTransition.Terminal,
                        terminalState: runtimeState.TerminalState,
                        reason: invalidApprovalMessage);

                    var invalidApprovalResult = BuildLoopTerminalResult(
                        request,
                        SafetyDisposition.Denied,
                        SafetyRiskLevel.High,
                        status: "Denied",
                        selectedTool: "None",
                        decisionText: string.IsNullOrWhiteSpace(decision.DecisionText)
                            ? BuildDecisionTextFromNextAction(nextAction)
                            : decision.DecisionText,
                        toolExecutionResult: "Not executed",
                        invalidApprovalMessage,
                        runtimeState);

                    await LogDecisionLoopStepAsync(
                        request,
                        safety,
                        riskLevel,
                        currentContext,
                        decision,
                        runtimeState,
                        transition: DecisionLoopTransition.Terminal,
                        invalidApprovalMessage,
                        outcome: "Blocked",
                        selectedTool: "None",
                        executionMode: "not-executed",
                        cancellationToken);

                    await LogEventAsync(
                        request,
                        "ExecutionBlocked",
                        invalidApprovalResult.Safety,
                        invalidApprovalResult.RiskLevel,
                        invalidApprovalResult.SelectedTool,
                        "not-executed",
                        invalidApprovalResult.Status,
                        invalidApprovalResult.Message,
                        approvalPath ? "Approved" : string.Empty,
                        cancellationToken,
                        currentContext,
                        BuildDecisionLoopFlags(runtimeState));

                    await LogEventAsync(
                        request,
                        "CommandCompleted",
                        invalidApprovalResult.Safety,
                        invalidApprovalResult.RiskLevel,
                        invalidApprovalResult.SelectedTool,
                        "not-executed",
                        invalidApprovalResult.Status,
                        invalidApprovalResult.Message,
                        approvalPath ? "Approved" : string.Empty,
                        cancellationToken,
                        currentContext,
                        BuildDecisionLoopFlags(runtimeState));

                    return invalidApprovalResult;
                }

                var approvalDecision = RewriteAskApprovalDecisionAsExecuteAction(decision);
                var approvalSafetyInterception = await TryInterceptStepSafetyAsync(
                    request,
                    safety,
                    riskLevel,
                    approvalPath,
                    currentContext,
                    approvalDecision,
                    runtimeState,
                    remainingApprovedStepApprovalKey,
                    cancellationToken,
                    modelRequestedApproval: true);
                if (approvalSafetyInterception.ConsumeApprovedStepKey)
                {
                    remainingApprovedStepApprovalKey = null;
                }

                if (approvalSafetyInterception.Result is not null)
                {
                    return approvalSafetyInterception.Result;
                }

                decision = approvalDecision;
                nextAction = decision.NextActionDecision!;
            }

            if (nextAction.ExecuteAction is not null)
            {
                runtimeState.LastExecutableAction = nextAction.ExecuteAction;
            }

            if (nextAction.Kind == DecisionKind.AskObserve)
            {
                var observeMessage = nextAction.AskObserve is null
                    ? "Model requested additional observation before execution."
                    : string.IsNullOrWhiteSpace(nextAction.AskObserve.ObservationHint)
                        ? $"Model requested observation: {nextAction.AskObserve.ObservationRequest}"
                        : $"Model requested observation: {nextAction.AskObserve.ObservationRequest}. Hint: {nextAction.AskObserve.ObservationHint}";

                runtimeState.LastExecutionFeedback = new DecisionExecutionFeedback
                {
                    ExecutedActionSummary = "observe-refresh",
                    ExecutionSucceeded = false,
                    ExecutionFailed = false,
                    ExecutionBlocked = false,
                    ExecutionMessage = observeMessage,
                    NormalizedOutcome = DecisionExecutionOutcome.NotExecuted,
                    ProducedResult = null,
                    ProducedTarget = null,
                    FailureCategory = DecisionFailureCategory.ObservationRequired,
                    BlockedReason = null,
                    Verification = new DecisionVerificationSummary
                    {
                        Status = null,
                        Summary = "Observation refresh requested.",
                        Reason = "Observation refresh requested.",
                        TargetReached = null
                    },
                    Approval = null
                };

                UpdateRuntimeVerificationState(
                    runtimeState,
                    runtimeState.LastExecutionFeedback.Verification,
                    runtimeState.LastExecutionFeedback.ProducedTarget);
                runtimeState.BlockedState = DecisionRuntimeBlockedState.None;
                runtimeState.GoalStillActive = true;
                runtimeState.Outcome = DecisionRuntimeOutcome.InProgress;
                runtimeState.TerminationReason = DecisionRuntimeTerminationReason.None;
                runtimeState.LastObservationSummary = observeMessage;

                AppendDecisionCycleStep(
                    runtimeState,
                    decisionKind: DecisionKind.AskObserve,
                    actionType: null,
                    targetSummary: null,
                    outcome: DecisionExecutionOutcome.NotExecuted,
                    transition: DecisionLoopTransition.RefreshObservation,
                    terminalState: DecisionLoopTerminalState.None,
                    reason: observeMessage);

                await LogDecisionLoopStepAsync(
                    request,
                    safety,
                    riskLevel,
                    currentContext,
                    decision,
                    runtimeState,
                    transition: DecisionLoopTransition.RefreshObservation,
                    observeMessage,
                    outcome: "ObserveRefresh",
                    selectedTool: "None",
                    executionMode: "not-executed",
                    cancellationToken);

                currentContext = await BuildExecutionContextAsync(request, safety, riskLevel, cancellationToken);
                continue;
            }

            if (nextAction.Kind == DecisionKind.Stop)
            {
                var stopDisposition = nextAction.Stop?.Disposition ?? StopDisposition.Blocked;
                var terminalState = MapStopDispositionToTerminalState(stopDisposition);
                var stopReason = nextAction.StopReason ?? nextAction.Stop?.StopReason ?? "Model requested stop.";

                runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
                runtimeState.TerminalState = terminalState;
                runtimeState.GoalStillActive = false;
                runtimeState.BlockedState = terminalState == DecisionLoopTerminalState.Completed
                    ? DecisionRuntimeBlockedState.None
                    : DecisionRuntimeBlockedState.Blocked;
                runtimeState.Outcome = terminalState switch
                {
                    DecisionLoopTerminalState.Completed => DecisionRuntimeOutcome.Completed,
                    DecisionLoopTerminalState.Aborted => DecisionRuntimeOutcome.Aborted,
                    _ => DecisionRuntimeOutcome.Blocked
                };
                runtimeState.TerminationReason = terminalState switch
                {
                    DecisionLoopTerminalState.Completed => DecisionRuntimeTerminationReason.GoalCompleted,
                    DecisionLoopTerminalState.Aborted => DecisionRuntimeTerminationReason.HardStop,
                    _ => DecisionRuntimeTerminationReason.NonRetryableFailure
                };

                AppendDecisionCycleStep(
                    runtimeState,
                    decisionKind: DecisionKind.Stop,
                    actionType: null,
                    targetSummary: null,
                    outcome: terminalState == DecisionLoopTerminalState.Completed
                        ? DecisionExecutionOutcome.Succeeded
                        : DecisionExecutionOutcome.Blocked,
                    transition: DecisionLoopTransition.Stop,
                    terminalState,
                    reason: stopReason);

                var stopStatus = terminalState == DecisionLoopTerminalState.Completed ? "Accepted" : "Denied";
                var stopResult = BuildLoopTerminalResult(
                    request,
                    safety,
                    riskLevel,
                    status: stopStatus,
                    selectedTool: "None",
                    decisionText: string.IsNullOrWhiteSpace(decision.DecisionText)
                        ? BuildDecisionTextFromNextAction(nextAction)
                        : decision.DecisionText,
                    toolExecutionResult: "Not executed",
                    stopReason,
                    runtimeState);

                await LogDecisionLoopStepAsync(
                    request,
                    safety,
                    riskLevel,
                    currentContext,
                    decision,
                    runtimeState,
                    transition: DecisionLoopTransition.Stop,
                    stopReason,
                    outcome: terminalState.ToString(),
                    selectedTool: "None",
                    executionMode: "not-executed",
                    cancellationToken);

                await LogEventAsync(
                    request,
                    "ExecutionStopped",
                    stopResult.Safety,
                    stopResult.RiskLevel,
                    stopResult.SelectedTool,
                    "not-executed",
                    stopResult.Status,
                    stopResult.Message,
                    approvalPath ? "Approved" : string.Empty,
                    cancellationToken,
                    currentContext,
                    BuildDecisionLoopFlags(runtimeState));

                await LogEventAsync(
                    request,
                    "CommandCompleted",
                    stopResult.Safety,
                    stopResult.RiskLevel,
                    stopResult.SelectedTool,
                    "not-executed",
                    stopResult.Status,
                    stopResult.Message,
                    approvalPath ? "Approved" : string.Empty,
                    cancellationToken,
                    currentContext,
                    BuildDecisionLoopFlags(runtimeState));

                return stopResult;
            }

            if (nextAction.Kind == DecisionKind.Retry)
            {
                if (runtimeState.RetryCount >= runtimeState.MaxRetryLimit)
                {
                    var retryLimitReason = nextAction.RetryReason ?? nextAction.Retry?.RetryReason ?? "Retry limit reached.";

                    runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
                    runtimeState.TerminalState = DecisionLoopTerminalState.Blocked;
                    runtimeState.BlockedState = DecisionRuntimeBlockedState.RetryLimitReached;
                    runtimeState.GoalStillActive = false;
                    runtimeState.Outcome = DecisionRuntimeOutcome.Blocked;
                    runtimeState.TerminationReason = DecisionRuntimeTerminationReason.RetryLimitReached;
                    runtimeState.LastExecutionFeedback = new DecisionExecutionFeedback
                    {
                        ExecutedActionSummary = "retry-limit",
                        ExecutionSucceeded = false,
                        ExecutionFailed = true,
                        ExecutionBlocked = true,
                        ExecutionMessage = retryLimitReason,
                        NormalizedOutcome = DecisionExecutionOutcome.Blocked,
                        ProducedResult = null,
                        ProducedTarget = null,
                        FailureCategory = DecisionFailureCategory.RetryLimitReached,
                        BlockedReason = retryLimitReason,
                        Verification = new DecisionVerificationSummary
                        {
                            Status = null,
                            Summary = "Retry limit reached.",
                            Reason = "Retry limit reached.",
                            TargetReached = false
                        },
                        Approval = null
                    };
                    UpdateRuntimeVerificationState(
                        runtimeState,
                        runtimeState.LastExecutionFeedback.Verification,
                        runtimeState.LastExecutionFeedback.ProducedTarget);

                    AppendDecisionCycleStep(
                        runtimeState,
                        decisionKind: DecisionKind.Retry,
                        actionType: runtimeState.LastExecutableAction?.ActionType,
                        targetSummary: runtimeState.LastExecutableAction?.Target.Reference,
                        outcome: DecisionExecutionOutcome.Blocked,
                        transition: DecisionLoopTransition.Terminal,
                        terminalState: runtimeState.TerminalState,
                        reason: retryLimitReason);

                    var retryLimitResult = BuildLoopTerminalResult(
                        request,
                        safety,
                        riskLevel,
                        status: "Denied",
                        selectedTool: "None",
                        decisionText: string.IsNullOrWhiteSpace(decision.DecisionText)
                            ? BuildDecisionTextFromNextAction(nextAction)
                            : decision.DecisionText,
                        toolExecutionResult: "Not executed",
                        retryLimitReason,
                        runtimeState);

                    await LogDecisionLoopStepAsync(
                        request,
                        safety,
                        riskLevel,
                        currentContext,
                        decision,
                        runtimeState,
                        transition: DecisionLoopTransition.Terminal,
                        retryLimitReason,
                        outcome: "RetryLimitReached",
                        selectedTool: "None",
                        executionMode: "not-executed",
                        cancellationToken);

                    await LogEventAsync(
                        request,
                        "CommandCompleted",
                        retryLimitResult.Safety,
                        retryLimitResult.RiskLevel,
                        retryLimitResult.SelectedTool,
                        "not-executed",
                        retryLimitResult.Status,
                        retryLimitResult.Message,
                        approvalPath ? "Approved" : string.Empty,
                        cancellationToken,
                        currentContext,
                        BuildDecisionLoopFlags(runtimeState));

                    return retryLimitResult;
                }

                if (!TryResolveRetryAction(nextAction, runtimeState.LastExecutableAction, out var retryAction, out var retryReason))
                {
                    runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
                    runtimeState.TerminalState = DecisionLoopTerminalState.Blocked;
                    runtimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
                    runtimeState.GoalStillActive = false;
                    runtimeState.Outcome = DecisionRuntimeOutcome.Blocked;
                    runtimeState.TerminationReason = DecisionRuntimeTerminationReason.NonRetryableFailure;

                    AppendDecisionCycleStep(
                        runtimeState,
                        decisionKind: DecisionKind.Retry,
                        actionType: null,
                        targetSummary: null,
                        outcome: DecisionExecutionOutcome.Blocked,
                        transition: DecisionLoopTransition.Terminal,
                        terminalState: runtimeState.TerminalState,
                        reason: retryReason);

                    var invalidRetryResult = BuildLoopTerminalResult(
                        request,
                        safety,
                        riskLevel,
                        status: "Denied",
                        selectedTool: "None",
                        decisionText: string.IsNullOrWhiteSpace(decision.DecisionText)
                            ? BuildDecisionTextFromNextAction(nextAction)
                            : decision.DecisionText,
                        toolExecutionResult: "Not executed",
                        retryReason,
                        runtimeState);

                    await LogDecisionLoopStepAsync(
                        request,
                        safety,
                        riskLevel,
                        currentContext,
                        decision,
                        runtimeState,
                        transition: DecisionLoopTransition.Terminal,
                        retryReason,
                        outcome: "Failed",
                        selectedTool: "None",
                        executionMode: "not-executed",
                        cancellationToken);

                    await LogEventAsync(
                        request,
                        "CommandCompleted",
                        invalidRetryResult.Safety,
                        invalidRetryResult.RiskLevel,
                        invalidRetryResult.SelectedTool,
                        "not-executed",
                        invalidRetryResult.Status,
                        invalidRetryResult.Message,
                        approvalPath ? "Approved" : string.Empty,
                        cancellationToken,
                        currentContext,
                        BuildDecisionLoopFlags(runtimeState));

                    return invalidRetryResult;
                }

                runtimeState.RetryCount++;
                runtimeState.RuntimeSession.RetryCount = runtimeState.RetryCount;
                runtimeState.BlockedState = DecisionRuntimeBlockedState.Retrying;
                runtimeState.Outcome = DecisionRuntimeOutcome.RetryableFailure;
                runtimeState.TerminationReason = DecisionRuntimeTerminationReason.None;
                runtimeState.LastExecutableAction = retryAction;

                var retryDecision = NormalizeDecision(new AiDecision
                {
                    SelectedToolName = MapActionTypeToToolName(retryAction.ActionType),
                    ToolArgument = ExtractToolArgumentFromExecuteAction(retryAction),
                    DecisionText = string.IsNullOrWhiteSpace(decision.DecisionText)
                        ? BuildDecisionTextFromNextAction(nextAction)
                        : decision.DecisionText,
                    NextActionDecision = new NextActionDecision
                    {
                        Kind = DecisionKind.ExecuteAction,
                        Message = string.IsNullOrWhiteSpace(nextAction.Message)
                            ? "Retrying previous action."
                            : nextAction.Message,
                        Rationale = nextAction.Rationale,
                        Confidence = nextAction.Confidence,
                        ExecuteAction = retryAction,
                        Metadata = nextAction.Metadata
                    },
                    IsLegacyFallback = decision.IsLegacyFallback
                });

                var retrySafetyInterception = await TryInterceptStepSafetyAsync(
                    request,
                    safety,
                    riskLevel,
                    approvalPath,
                    currentContext,
                    retryDecision,
                    runtimeState,
                    remainingApprovedStepApprovalKey,
                    cancellationToken);
                if (retrySafetyInterception.ConsumeApprovedStepKey)
                {
                    remainingApprovedStepApprovalKey = null;
                }

                if (retrySafetyInterception.Result is not null)
                {
                    return retrySafetyInterception.Result;
                }

                var retryExecution = await ExecuteLoopDecisionStepAsync(
                    request,
                    safety,
                    riskLevel,
                    approvalPath,
                    currentContext,
                    retryDecision,
                    cancellationToken,
                    snapshotUsed,
                    snapshotFallback,
                    targetReasonNormalized,
                    targetReasonSource,
                    snapshotContextUsed,
                    snapshotAdapterPreserved,
                    snapshotObservationPreserved,
                    decisionInputBundle,
                    decisionSummary,
                    aiDecisionInput);

                runtimeState.LastExecutionFeedback = retryExecution.Feedback;
                UpdateRuntimeVerificationState(
                    runtimeState,
                    retryExecution.Feedback.Verification,
                    retryExecution.Feedback.ProducedTarget);
                runtimeState.LastObservationSummary = retryExecution.Feedback.ExecutionMessage;
                runtimeState.GoalStillActive = true;
                runtimeState.CurrentDecision = retryDecision.NextActionDecision;
                var retryExecutionMode = retryExecution.CommandResult.ToolExecutionResult == "Not executed" ? "not-executed" : "real";

                await LogDecisionLoopVerificationAsync(
                    request,
                    safety,
                    riskLevel,
                    currentContext,
                    retryDecision,
                    runtimeState,
                    retryExecution.Feedback,
                    retryExecution.CommandResult.SelectedTool,
                    retryExecutionMode,
                    cancellationToken);

                AppendDecisionCycleStep(
                    runtimeState,
                    decisionKind: DecisionKind.Retry,
                    actionType: retryAction.ActionType,
                    targetSummary: retryAction.Target.Reference,
                    outcome: retryExecution.Feedback.NormalizedOutcome,
                    transition: DecisionLoopTransition.RetryExecution,
                    terminalState: DecisionLoopTerminalState.None,
                    reason: retryExecution.Feedback.ExecutionMessage);

                await LogDecisionLoopStepAsync(
                    request,
                    safety,
                    riskLevel,
                    currentContext,
                    retryDecision,
                    runtimeState,
                    transition: DecisionLoopTransition.RetryExecution,
                    retryExecution.Feedback.ExecutionMessage,
                    outcome: retryExecution.Feedback.NormalizedOutcome.ToString(),
                    selectedTool: retryExecution.CommandResult.SelectedTool,
                    executionMode: retryExecutionMode,
                    cancellationToken);

                currentContext = await RefreshDecisionLoopContextAsync(
                    request,
                    safety,
                    riskLevel,
                    currentContext,
                    retryExecution.Feedback,
                    cancellationToken);

                continue;
            }

            var stepSafetyInterception = await TryInterceptStepSafetyAsync(
                request,
                safety,
                riskLevel,
                approvalPath,
                currentContext,
                decision,
                runtimeState,
                remainingApprovedStepApprovalKey,
                cancellationToken);
            if (stepSafetyInterception.ConsumeApprovedStepKey)
            {
                remainingApprovedStepApprovalKey = null;
            }

            if (stepSafetyInterception.Result is not null)
            {
                return stepSafetyInterception.Result;
            }

            var stepExecution = await ExecuteLoopDecisionStepAsync(
                request,
                safety,
                riskLevel,
                approvalPath,
                currentContext,
                decision,
                cancellationToken,
                snapshotUsed,
                snapshotFallback,
                targetReasonNormalized,
                targetReasonSource,
                snapshotContextUsed,
                snapshotAdapterPreserved,
                snapshotObservationPreserved,
                decisionInputBundle,
                decisionSummary,
                aiDecisionInput);

            runtimeState.LastExecutionFeedback = stepExecution.Feedback;
            UpdateRuntimeVerificationState(
                runtimeState,
                stepExecution.Feedback.Verification,
                stepExecution.Feedback.ProducedTarget);
            runtimeState.LastObservationSummary = stepExecution.Feedback.ExecutionMessage;
            var operationalObservation = ObservationRolePartition.ToOperationalSnapshot(currentContext.RichObservation);
            var postExecutionTransition = DeterminePostExecutionTransition(
                runtimeState,
                nextAction,
                stepExecution.Feedback,
                operationalObservation?.RuntimeState,
                operationalObservation);

            currentContext = ApplyObservationRuntimeTransitionMetadata(currentContext, postExecutionTransition);

            if (postExecutionTransition.IncrementRetryCount)
            {
                runtimeState.RetryCount++;
                runtimeState.RuntimeSession.RetryCount = runtimeState.RetryCount;
            }

            runtimeState.BlockedState = postExecutionTransition.BlockedState;
            runtimeState.GoalStillActive = postExecutionTransition.ContinueLoop;
            runtimeState.Outcome = postExecutionTransition.Outcome;
            runtimeState.TerminationReason = postExecutionTransition.TerminationReason;

            if (postExecutionTransition.ContinueLoop)
            {
                runtimeState.CompletionState = DecisionRuntimeCompletionState.Running;
                runtimeState.TerminalState = DecisionLoopTerminalState.None;
            }
            else
            {
                runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
                runtimeState.TerminalState = postExecutionTransition.TerminalState;
                runtimeState.GoalStillActive = false;
            }

            var stepExecutionMode = stepExecution.CommandResult.ToolExecutionResult == "Not executed" ? "not-executed" : "real";

            await LogDecisionLoopVerificationAsync(
                request,
                safety,
                riskLevel,
                currentContext,
                decision,
                runtimeState,
                stepExecution.Feedback,
                stepExecution.CommandResult.SelectedTool,
                stepExecutionMode,
                cancellationToken);

            AppendDecisionCycleStep(
                runtimeState,
                decisionKind: nextAction.Kind,
                actionType: nextAction.ExecuteAction?.ActionType,
                targetSummary: nextAction.ExecuteAction?.Target.Reference,
                outcome: stepExecution.Feedback.NormalizedOutcome,
                transition: postExecutionTransition.Transition,
                terminalState: postExecutionTransition.ContinueLoop ? DecisionLoopTerminalState.None : runtimeState.TerminalState,
                reason: stepExecution.Feedback.ExecutionMessage);

            await LogDecisionLoopStepAsync(
                request,
                safety,
                riskLevel,
                currentContext,
                decision,
                runtimeState,
                transition: postExecutionTransition.Transition,
                stepExecution.Feedback.ExecutionMessage,
                outcome: stepExecution.Feedback.NormalizedOutcome.ToString(),
                selectedTool: stepExecution.CommandResult.SelectedTool,
                executionMode: stepExecutionMode,
                cancellationToken);

            if (!postExecutionTransition.ContinueLoop)
            {
                return AttachRuntimeState(stepExecution.CommandResult, runtimeState);
            }

            currentContext = await RefreshDecisionLoopContextAsync(
                request,
                safety,
                riskLevel,
                currentContext,
                stepExecution.Feedback,
                cancellationToken);
        }

            runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
            runtimeState.TerminalState = DecisionLoopTerminalState.MaxStepsReached;
            runtimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
            runtimeState.GoalStillActive = false;
            runtimeState.Outcome = DecisionRuntimeOutcome.Blocked;
            runtimeState.TerminationReason = DecisionRuntimeTerminationReason.MaxStepLimitReached;

            var maxStepMessage = $"Decision loop reached the configured step limit ({runtimeState.MaxStepLimit}).";

            AppendDecisionCycleStep(
                runtimeState,
                decisionKind: runtimeState.CurrentDecision?.Kind ?? DecisionKind.Stop,
                actionType: runtimeState.CurrentDecision?.ExecuteAction?.ActionType,
                targetSummary: runtimeState.CurrentDecision?.ExecuteAction?.Target.Reference,
                outcome: DecisionExecutionOutcome.Blocked,
                transition: DecisionLoopTransition.Terminal,
                terminalState: runtimeState.TerminalState,
                reason: maxStepMessage);

            await LogStepSubmittedAsync(
                request,
                safety,
                riskLevel,
                currentContext,
                currentDecision,
                runtimeState,
                selectedTool: "None",
                executionMode: "not-executed",
                approvalDecision: approvalPath ? "Approved" : string.Empty,
                cancellationToken);

            var maxStepResult = BuildLoopTerminalResult(
                request,
                safety,
                riskLevel,
                status: "Denied",
                selectedTool: "None",
                decisionText: "Decision loop terminated.",
                toolExecutionResult: "Not executed",
                maxStepMessage,
                runtimeState);

            await LogEventAsync(
                request,
                "CommandCompleted",
                maxStepResult.Safety,
                maxStepResult.RiskLevel,
                maxStepResult.SelectedTool,
                "not-executed",
                maxStepResult.Status,
                maxStepResult.Message,
                approvalPath ? "Approved" : string.Empty,
                cancellationToken,
                currentContext,
                BuildDecisionLoopFlags(runtimeState));

            return maxStepResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
            runtimeState.TerminalState = DecisionLoopTerminalState.Aborted;
            runtimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
            runtimeState.GoalStillActive = false;
            runtimeState.Outcome = DecisionRuntimeOutcome.Aborted;
            runtimeState.TerminationReason = DecisionRuntimeTerminationReason.FatalException;

            var fatalMessage = $"Decision loop aborted due to fatal runtime exception: {ex.Message}";

            AppendDecisionCycleStep(
                runtimeState,
                decisionKind: runtimeState.CurrentDecision?.Kind ?? DecisionKind.Stop,
                actionType: runtimeState.CurrentDecision?.ExecuteAction?.ActionType,
                targetSummary: runtimeState.CurrentDecision?.ExecuteAction?.Target.Reference,
                outcome: DecisionExecutionOutcome.Failed,
                transition: DecisionLoopTransition.Terminal,
                terminalState: runtimeState.TerminalState,
                reason: fatalMessage);

            await LogStepSubmittedAsync(
                request,
                safety,
                riskLevel,
                currentContext,
                currentDecision,
                runtimeState,
                selectedTool: "None",
                executionMode: "not-executed",
                approvalDecision: approvalPath ? "Approved" : string.Empty,
                cancellationToken);

            var fatalResult = BuildLoopTerminalResult(
                request,
                safety,
                riskLevel,
                status: "Denied",
                selectedTool: "None",
                decisionText: "Decision loop aborted.",
                toolExecutionResult: "Not executed",
                message: fatalMessage,
                runtimeState: runtimeState);

            await LogEventAsync(
                request,
                "CommandCompleted",
                fatalResult.Safety,
                fatalResult.RiskLevel,
                fatalResult.SelectedTool,
                "not-executed",
                fatalResult.Status,
                fatalResult.Message,
                approvalPath ? "Approved" : string.Empty,
                cancellationToken,
                currentContext,
                BuildDecisionLoopFlags(runtimeState));

            return fatalResult;
        }
    }

    private async Task<DecisionLoopExecutionResult> ExecuteLoopDecisionStepAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        bool approvalPath,
        AgentExecutionContext executionContext,
        AiDecision decision,
        CancellationToken cancellationToken,
        bool? snapshotUsed,
        bool? snapshotFallback,
        bool? targetReasonNormalized,
        string? targetReasonSource,
        bool? snapshotContextUsed,
        bool? snapshotAdapterPreserved,
        bool? snapshotObservationPreserved,
        DecisionInputBundle decisionInputBundle,
        DecisionSummary decisionSummary,
        AiDecisionInput aiDecisionInput)
    {
        CommandResult result;

        {
            {
                var capabilityResult = await TryExecuteCapabilityPathAsync(
                    request,
                    safety,
                    riskLevel,
                    executionContext,
                    decision,
                    approvalPath,
                    cancellationToken,
                    snapshotUsed,
                    snapshotFallback,
                    targetReasonNormalized,
                    targetReasonSource,
                    snapshotContextUsed,
                    snapshotAdapterPreserved,
                    snapshotObservationPreserved,
                    decisionInputBundle,
                    decisionSummary,
                    aiDecisionInput);

                if (capabilityResult is not null)
                {
                    result = capabilityResult;
                }
                else
                {
                    if (_targetGrounder is not null &&
                        RequiresExecutableLaunchGrounding(decision, executionContext) &&
                        !HasExecutableLaunchGrounding(executionContext.PrimaryTargetGrounding))
                    {
                        result = await BuildLaunchGroundingBlockedResultAsync(
                            request,
                            safety,
                            riskLevel,
                            executionContext,
                            decision,
                            approvalPath,
                            cancellationToken,
                            snapshotUsed,
                            snapshotFallback,
                            targetReasonNormalized,
                            targetReasonSource,
                            snapshotContextUsed,
                            snapshotAdapterPreserved,
                            snapshotObservationPreserved,
                            decisionInputBundle,
                            decisionSummary,
                            aiDecisionInput);
                    }
                    else
                    {
                        result = await ExecuteToolPathAsync(
                            request,
                            safety,
                            riskLevel,
                            approvalPath,
                            executionContext,
                            cancellationToken,
                            decision,
                            snapshotUsed,
                            snapshotFallback,
                            targetReasonNormalized,
                            targetReasonSource,
                            snapshotContextUsed,
                            snapshotAdapterPreserved,
                            snapshotObservationPreserved,
                            decisionInputBundle,
                            decisionSummary,
                            aiDecisionInput);
                    }
                }
            }
        }

        result = await ApplyExecutionDerivedVerificationAsync(
            decision.NextActionDecision,
            executionContext,
            result,
            cancellationToken);

        var feedback = BuildDecisionExecutionFeedback(decision.NextActionDecision, result);

        return new DecisionLoopExecutionResult
        {
            CommandResult = result,
            Feedback = feedback
        };
    }

    private async Task<StepSafetyInterceptionResult> TryInterceptStepSafetyAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        bool approvalPath,
        AgentExecutionContext executionContext,
        AiDecision decision,
        DecisionCycleRuntimeState runtimeState,
        string? approvedStepApprovalKey,
        CancellationToken cancellationToken,
        bool modelRequestedApproval = false)
    {
        var executeAction = decision.NextActionDecision?.ExecuteAction;
        if (executeAction is null)
        {
            return new StepSafetyInterceptionResult();
        }

        var stepSafetyRequest = new StepSafetyRequest
        {
            Source = StepSafetyRequestSource.DecisionLoop,
            CommandText = request.UserInput,
            StepIndex = runtimeState.CurrentStepIndex,
            ExecuteAction = executeAction
        };

        var stepSafetyDecision = await EvaluateStepSafetyAsync(stepSafetyRequest, cancellationToken);
        var approvalSnapshotMatch = approvalPath &&
                                    stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval &&
                                    !string.IsNullOrWhiteSpace(stepSafetyDecision.ApprovalKey) &&
                                    !string.IsNullOrWhiteSpace(approvedStepApprovalKey) &&
                                    stepSafetyDecision.ApprovalKey.Equals(approvedStepApprovalKey, StringComparison.Ordinal);

        var selectedTool = ResolveDecisionToolNameForDisplay(decision);
        if (string.IsNullOrWhiteSpace(selectedTool))
        {
            selectedTool = "None";
        }

        await LogEventAsync(
            request,
            "PolicyEvaluated",
            stepSafetyDecision.Disposition.ToString(),
            stepSafetyDecision.RiskLevel.ToString(),
            selectedTool,
            "not-executed",
            "Evaluated",
            stepSafetyDecision.Reason,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            executionContext,
            BuildPolicyEvaluationMetadata(
                "step",
                stepSafetyRequest,
                stepSafetyDecision,
                approvalSnapshotMatch,
                DetermineApprovalAuthorityOutcome(modelRequestedApproval, stepSafetyDecision)));

        if (approvalSnapshotMatch || stepSafetyDecision.Disposition == SafetyDisposition.Allowed)
        {
            return new StepSafetyInterceptionResult
            {
                ConsumeApprovedStepKey = approvalSnapshotMatch
            };
        }

        var decisionText = string.IsNullOrWhiteSpace(decision.DecisionText)
            ? BuildDecisionTextFromNextAction(decision.NextActionDecision!)
            : decision.DecisionText;
        var transition = stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval
            ? DecisionLoopTransition.AwaitApproval
            : DecisionLoopTransition.Terminal;

        runtimeState.AwaitingApproval = stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval;
        runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
        runtimeState.TerminalState = stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval
            ? DecisionLoopTerminalState.AwaitingApproval
            : DecisionLoopTerminalState.Aborted;
        runtimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
        runtimeState.GoalStillActive = stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval;
        runtimeState.Outcome = stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval
            ? DecisionRuntimeOutcome.Blocked
            : DecisionRuntimeOutcome.Aborted;
        runtimeState.TerminationReason = stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval
            ? DecisionRuntimeTerminationReason.AwaitingApproval
            : DecisionRuntimeTerminationReason.SafetyDenied;

        CommandResult result;
        if (stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval)
        {
            var pendingSnapshot = BuildPendingApprovalSnapshot(
                request,
                decision,
                executionContext.ResolvedTargets,
                executionContext.ContextAdapter,
                BuildPendingObservationSummary(executionContext.Observation),
                runtimeState);

            result = new CommandResult
            {
                Status = "PendingApproval",
                Safety = SafetyDisposition.RequiresApproval.ToString(),
                RiskLevel = stepSafetyDecision.RiskLevel.ToString(),
                Decision = decisionText,
                SelectedTool = selectedTool,
                ToolExecutionResult = "Not executed",
                Message = stepSafetyDecision.Reason,
                PendingApprovalSnapshot = pendingSnapshot,
                RuntimeId = runtimeState.RuntimeId,
                RuntimeStepCount = runtimeState.CurrentStepIndex,
                RuntimeTerminalState = runtimeState.TerminalState,
                DecisionCycleState = runtimeState
            };
        }
        else
        {
            result = BuildLoopTerminalResult(
                request,
                SafetyDisposition.Denied,
                stepSafetyDecision.RiskLevel,
                status: "Denied",
                selectedTool,
                decisionText,
                toolExecutionResult: "Not executed",
                message: stepSafetyDecision.Reason,
                runtimeState);
        }

        var feedback = BuildDecisionExecutionFeedback(decision.NextActionDecision, result);
        runtimeState.LastExecutionFeedback = feedback;
        UpdateRuntimeVerificationState(
            runtimeState,
            feedback.Verification,
            feedback.ProducedTarget);
        runtimeState.LastObservationSummary = feedback.ExecutionMessage;

        AppendDecisionCycleStep(
            runtimeState,
            decisionKind: decision.NextActionDecision!.Kind,
            actionType: executeAction.ActionType,
            targetSummary: executeAction.Target.Reference,
            outcome: feedback.NormalizedOutcome,
            transition,
            terminalState: runtimeState.TerminalState,
            reason: feedback.ExecutionMessage);

        await LogDecisionLoopVerificationAsync(
            request,
            safety,
            riskLevel,
            executionContext,
            decision,
            runtimeState,
            feedback,
            result.SelectedTool,
            executionMode: "not-executed",
            cancellationToken);

        await LogDecisionLoopStepAsync(
            request,
            safety,
            riskLevel,
            executionContext,
            decision,
            runtimeState,
            transition,
            feedback.ExecutionMessage,
            feedback.NormalizedOutcome.ToString(),
            result.SelectedTool,
            executionMode: "not-executed",
            cancellationToken);

        var finalResult = AttachRuntimeState(result, runtimeState);
        if (stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval)
        {
            await LogEventAsync(
                request,
                "ApprovalDecision",
                finalResult.Safety,
                finalResult.RiskLevel,
                finalResult.SelectedTool,
                "not-executed",
                "Requested",
                "Step execution paused and awaiting approval.",
                "Requested",
                cancellationToken,
                executionContext,
                BuildDecisionLoopFlags(runtimeState));

            await LogEventAsync(
                request,
                "ApprovalRequired",
                finalResult.Safety,
                finalResult.RiskLevel,
                finalResult.SelectedTool,
                "not-executed",
                finalResult.Status,
                finalResult.Message,
                string.Empty,
                cancellationToken,
                executionContext,
                BuildDecisionLoopFlags(runtimeState));
        }
        else
        {
            await LogEventAsync(
                request,
                "ExecutionBlocked",
                finalResult.Safety,
                finalResult.RiskLevel,
                finalResult.SelectedTool,
                "not-executed",
                finalResult.Status,
                finalResult.Message,
                approvalPath ? "Approved" : string.Empty,
                cancellationToken,
                executionContext,
                BuildDecisionLoopFlags(runtimeState));
        }

        await LogEventAsync(
            request,
            "CommandCompleted",
            finalResult.Safety,
            finalResult.RiskLevel,
            finalResult.SelectedTool,
            "not-executed",
            finalResult.Status,
            finalResult.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            executionContext,
            BuildDecisionLoopFlags(runtimeState));

        return new StepSafetyInterceptionResult
        {
            Result = finalResult
        };
    }

    private async Task<AgentExecutionContext> RefreshDecisionLoopContextAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        AgentExecutionContext currentContext,
        DecisionExecutionFeedback feedback,
        CancellationToken cancellationToken)
    {
        try
        {
            var refreshedContext = await BuildExecutionContextAsync(request, safety, riskLevel, cancellationToken);
            return ApplyRuntimeState(refreshedContext, BuildRuntimeStateFromFeedback(feedback));
        }
        catch
        {
            return ApplyRuntimeState(currentContext, BuildRuntimeStateFromFeedback(feedback));
        }
    }

    private async Task<StepSafetyDecision> EvaluateStepSafetyAsync(
        StepSafetyRequest request,
        CancellationToken cancellationToken)
    {
        if (_stepSafetyEvaluator is null)
        {
            return new StepSafetyDecision
            {
                Disposition = SafetyDisposition.Allowed,
                RiskLevel = SafetyRiskLevel.Low,
                Reason = "Step safety evaluator unavailable. Defaulted to allow."
            };
        }

        return await _stepSafetyEvaluator.EvaluateAsync(request, cancellationToken);
    }

    private async Task<CommandResult> ApplyExecutionDerivedVerificationAsync(
        NextActionDecision? nextActionDecision,
        AgentExecutionContext executionContext,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        var actionType = nextActionDecision?.ExecuteAction?.ActionType;
        var verificationPolicy = ActionVerificationPolicyResolver.Resolve(actionType);
        if (verificationPolicy.Mode == ActionVerificationMode.StrictExternal)
        {
            return await ApplyStrictExternalVerificationForToolPathAsync(
                nextActionDecision?.ExecuteAction,
                executionContext,
                result,
                verificationPolicy,
                cancellationToken);
        }

        if (verificationPolicy.Mode != ActionVerificationMode.ExecutionDerived)
        {
            return result;
        }

        if (result.VerificationStatus.HasValue)
        {
            return result;
        }

        var normalizedOutcome = NormalizeCommandResultOutcome(result);
        if (normalizedOutcome is DecisionExecutionOutcome.Failed or DecisionExecutionOutcome.Blocked)
        {
            return AttachVerificationToCommandResult(
                result,
                VerificationStatus.NotVerified,
                "Execution-derived verification failed because action execution did not succeed.");
        }

        if (normalizedOutcome == DecisionExecutionOutcome.NotExecuted)
        {
            return AttachVerificationToCommandResult(
                result,
                VerificationStatus.Unsupported,
                "Execution-derived verification is unsupported because no action was executed.");
        }

        ObservationSnapshot? postObservation = null;
        var postObservationCaptured = false;

        try
        {
            postObservation = await _observationProvider.CaptureAsync(cancellationToken);
            postObservationCaptured = true;
        }
        catch
        {
            postObservation = null;
        }

        var evaluation = EvaluateExecutionDerivedVerification(
            verificationPolicy,
            actionType,
            nextActionDecision?.ExecuteAction,
            result,
            executionContext.Observation,
            postObservation,
            postObservationCaptured);

        return AttachVerificationToCommandResult(result, evaluation.Status, evaluation.Reason);
    }

    private async Task<CommandResult> ApplyStrictExternalVerificationForToolPathAsync(
        ExecuteActionPayload? executeAction,
        AgentExecutionContext executionContext,
        CommandResult result,
        ActionVerificationPolicy verificationPolicy,
        CancellationToken cancellationToken)
    {
        if (result.VerificationStatus.HasValue)
        {
            return result;
        }

        if (_executionVerifier is null)
        {
            return AttachVerificationToCommandResult(
                result,
                VerificationStatus.Unsupported,
                "Strict verification is unsupported because no execution verifier is configured.");
        }

        var normalizedOutcome = NormalizeCommandResultOutcome(result);
        if (normalizedOutcome is DecisionExecutionOutcome.Failed or DecisionExecutionOutcome.Blocked)
        {
            return AttachVerificationToCommandResult(
                result,
                VerificationStatus.NotVerified,
                "Strict verification failed because action execution did not succeed.");
        }

        if (normalizedOutcome == DecisionExecutionOutcome.NotExecuted)
        {
            return AttachVerificationToCommandResult(
                result,
                VerificationStatus.Unsupported,
                "Strict verification is unsupported because no action was executed.");
        }

        if (!TryBuildVerificationSpecifications(
                executeAction,
                verificationPolicy,
                out var specifications,
                out var specificationFailureReason))
        {
            return AttachVerificationToCommandResult(
                result,
                VerificationStatus.Unsupported,
                specificationFailureReason ?? "Strict verification specification could not be built for this action.");
        }

        ObservationSnapshot? postObservation = null;
        try
        {
            postObservation = await _observationProvider.CaptureAsync(cancellationToken);
        }
        catch
        {
            postObservation = null;
        }

        var verificationContext = new AgentExecutionContext
        {
            CorrelationId = executionContext.CorrelationId,
            RawInput = executionContext.RawInput,
            NormalizedInput = executionContext.NormalizedInput,
            DetectedIntent = executionContext.DetectedIntent,
            CreatedAtUtc = executionContext.CreatedAtUtc,
            SessionId = executionContext.SessionId,
            Observation = postObservation ?? executionContext.Observation,
            RichObservation = executionContext.RichObservation,
            ContextAdapter = executionContext.ContextAdapter,
            PrimaryTargetGrounding = executionContext.PrimaryTargetGrounding,
            RuntimeState = executionContext.RuntimeState,
            ResolvedTargets = executionContext.ResolvedTargets,
            Metadata = executionContext.Metadata
        };

        var verificationRequest = new VerificationRequest
        {
            CorrelationId = executionContext.CorrelationId.ToString("N"),
            Action = null,
            ExecutionContext = verificationContext,
            ExecutionResult = new ActionExecutionResult
            {
                Status = normalizedOutcome == DecisionExecutionOutcome.Succeeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed,
                Message = result.Message,
                OutputText = result.ToolExecutionResult,
                OutputData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["toolExecutionResult"] = result.ToolExecutionResult
                },
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow
            },
            Specifications = specifications,
            Metadata = BuildPreObservationMetadata(executionContext.Observation)
        };

        var verification = await _executionVerifier.VerifyAsync(verificationRequest, cancellationToken);
        return AttachVerificationToCommandResult(result, verification.Status, verification.Reason);
    }

    private static (VerificationStatus Status, string Reason) EvaluateExecutionDerivedVerification(
        ActionVerificationPolicy verificationPolicy,
        ActionType? actionType,
        ExecuteActionPayload? executeAction,
        CommandResult result,
        ObservationSnapshot? preObservation,
        ObservationSnapshot? postObservation,
        bool postObservationCaptured)
    {
        var targetReference = executeAction?.Target.Reference;

        return verificationPolicy.ExecutionDerivedStrategy switch
        {
            ExecutionDerivedVerificationStrategy.TextDelta => EvaluateTextDeltaVerification(
                actionType,
                result,
                preObservation,
                postObservation,
                postObservationCaptured),
            ExecutionDerivedVerificationStrategy.ContextTransition => EvaluateContextTransitionVerification(
                targetReference,
                preObservation,
                postObservation,
                postObservationCaptured),
            ExecutionDerivedVerificationStrategy.FileOpenEffect => EvaluateFileOpenVerification(
                result,
                targetReference,
                preObservation,
                postObservation,
                postObservationCaptured),
            ExecutionDerivedVerificationStrategy.KeyInteraction => EvaluateKeyInteractionVerification(
                actionType,
                result,
                preObservation,
                postObservation,
                postObservationCaptured),
            _ => (
                VerificationStatus.Unsupported,
                "Execution-derived verification strategy is unsupported for this action.")
        };
    }

    private static (VerificationStatus Status, string Reason) EvaluateTextDeltaVerification(
        ActionType? actionType,
        CommandResult result,
        ObservationSnapshot? preObservation,
        ObservationSnapshot? postObservation,
        bool postObservationCaptured)
    {
        if (HasTextSignalDelta(preObservation, postObservation))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded: text-related observation delta detected.");
        }

        if (HasForegroundContextDelta(preObservation, postObservation))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded: foreground context changed after text input.");
        }

        if (HasStrongPrimitiveDispatchEvidence(result, actionType))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded via primitive dispatch evidence fallback.");
        }

        return (
            VerificationStatus.Inconclusive,
            postObservationCaptured
                ? "Execution-derived verification is inconclusive: no observable text/context delta after input."
                : "Execution-derived verification is inconclusive: post-action observation is unavailable.");
    }

    private static (VerificationStatus Status, string Reason) EvaluateContextTransitionVerification(
        string? targetReference,
        ObservationSnapshot? preObservation,
        ObservationSnapshot? postObservation,
        bool postObservationCaptured)
    {
        if (HasForegroundContextDelta(preObservation, postObservation))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded: foreground context transition detected.");
        }

        if (MatchesTargetHint(postObservation, targetReference))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded: post-action foreground context matches target hint.");
        }

        return (
            VerificationStatus.Inconclusive,
            postObservationCaptured
                ? "Execution-derived verification is inconclusive: no observable navigation/context transition."
                : "Execution-derived verification is inconclusive: post-action observation is unavailable.");
    }

    private static (VerificationStatus Status, string Reason) EvaluateFileOpenVerification(
        CommandResult result,
        string? targetReference,
        ObservationSnapshot? preObservation,
        ObservationSnapshot? postObservation,
        bool postObservationCaptured)
    {
        if (HasForegroundContextDelta(preObservation, postObservation))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded: foreground context changed after file-open action.");
        }

        if (MatchesTargetHint(postObservation, targetReference))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded: foreground context matches opened file target hint.");
        }

        if (HasStrongFileFlowEvidence(result))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded via composed file-flow evidence fallback.");
        }

        return (
            VerificationStatus.Inconclusive,
            postObservationCaptured
                ? "Execution-derived verification is inconclusive: file-open action produced no observable context effect."
                : "Execution-derived verification is inconclusive: post-action observation is unavailable.");
    }

    private static (VerificationStatus Status, string Reason) EvaluateKeyInteractionVerification(
        ActionType? actionType,
        CommandResult result,
        ObservationSnapshot? preObservation,
        ObservationSnapshot? postObservation,
        bool postObservationCaptured)
    {
        if (HasForegroundContextDelta(preObservation, postObservation))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded: key interaction triggered a foreground context delta.");
        }

        if (HasStrongPrimitiveDispatchEvidence(result, actionType))
        {
            return (
                VerificationStatus.Verified,
                "Execution-derived verification succeeded via key dispatch evidence fallback.");
        }

        return (
            VerificationStatus.Inconclusive,
            postObservationCaptured
                ? "Execution-derived verification is inconclusive: key interaction produced no observable state delta."
                : "Execution-derived verification is inconclusive: post-action observation is unavailable.");
    }

    private static bool HasTextSignalDelta(ObservationSnapshot? preObservation, ObservationSnapshot? postObservation)
    {
        if (postObservation is null)
        {
            return false;
        }

        if (preObservation is null)
        {
            return !string.IsNullOrWhiteSpace(postObservation.SelectionTextPreview) ||
                   !string.IsNullOrWhiteSpace(postObservation.ClipboardTextPreview) ||
                   postObservation.HasSelection;
        }

        return preObservation.HasSelection != postObservation.HasSelection ||
               !StringsEqual(preObservation.SelectionTextPreview, postObservation.SelectionTextPreview) ||
               !StringsEqual(preObservation.ClipboardTextPreview, postObservation.ClipboardTextPreview);
    }

    private static bool HasForegroundContextDelta(ObservationSnapshot? preObservation, ObservationSnapshot? postObservation)
    {
        if (postObservation is null)
        {
            return false;
        }

        if (preObservation is null)
        {
            return !string.IsNullOrWhiteSpace(postObservation.ActiveProcessName) ||
                   postObservation.ActiveWindow?.Handle is long ||
                   !string.IsNullOrWhiteSpace(postObservation.ActiveWindow?.Title);
        }

        if (!StringsEqual(
                NormalizeProcessNameForComparison(preObservation.ActiveProcessName),
                NormalizeProcessNameForComparison(postObservation.ActiveProcessName)))
        {
            return true;
        }

        if (preObservation.ActiveWindow?.Handle != postObservation.ActiveWindow?.Handle)
        {
            return true;
        }

        return !StringsEqual(preObservation.ActiveWindow?.Title, postObservation.ActiveWindow?.Title);
    }

    private static bool MatchesTargetHint(ObservationSnapshot? postObservation, string? targetReference)
    {
        if (postObservation is null || string.IsNullOrWhiteSpace(targetReference))
        {
            return false;
        }

        var targetHint = BuildTargetHint(targetReference);
        if (string.IsNullOrWhiteSpace(targetHint))
        {
            return false;
        }

        var normalizedHint = targetHint.ToLowerInvariant();
        var activeProcessName = NormalizeProcessNameForComparison(postObservation.ActiveProcessName);
        if (!string.IsNullOrWhiteSpace(activeProcessName) &&
            activeProcessName.Contains(normalizedHint, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var activeWindowProcess = NormalizeProcessNameForComparison(postObservation.ActiveWindow?.ProcessName);
        if (!string.IsNullOrWhiteSpace(activeWindowProcess) &&
            activeWindowProcess.Contains(normalizedHint, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var activeWindowTitle = postObservation.ActiveWindow?.Title;
        return !string.IsNullOrWhiteSpace(activeWindowTitle) &&
               activeWindowTitle.Contains(targetHint, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTargetHint(string targetReference)
    {
        var normalizedTarget = targetReference.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return string.Empty;
        }

        var fileNameHint = System.IO.Path.GetFileNameWithoutExtension(normalizedTarget);
        if (!string.IsNullOrWhiteSpace(fileNameHint))
        {
            return fileNameHint.Trim().ToLowerInvariant();
        }

        return NormalizeProcessNameForComparison(normalizedTarget);
    }

    private static bool HasStrongPrimitiveDispatchEvidence(CommandResult result, ActionType? actionType)
    {
        if (!result.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(result.ToolExecutionResult) ||
            result.ToolExecutionResult.Equals("Not executed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (result.SelectedTool.Equals("TypeTextTool", StringComparison.OrdinalIgnoreCase) ||
            result.SelectedTool.Equals("PressKeyTool", StringComparison.OrdinalIgnoreCase) ||
            result.SelectedTool.Equals("PressShortcutTool", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (actionType is ActionType.InputText or ActionType.PressKey or ActionType.PressShortcut or ActionType.Confirm or ActionType.Cancel &&
            result.ToolExecutionResult.StartsWith("Real ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool HasStrongFileFlowEvidence(CommandResult result)
    {
        if (!result.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
            !result.SelectedTool.Equals("FileEndToEndFlow", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(result.ToolExecutionResult) ||
            result.ToolExecutionResult.Equals("Not executed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return result.Message.Contains("file verified and opened", StringComparison.OrdinalIgnoreCase) ||
               result.Message.Contains("focus attempt succeeded", StringComparison.OrdinalIgnoreCase) ||
               result.Message.Contains("file flow completed", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessNameForComparison(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.ToLowerInvariant();
    }

    private static bool StringsEqual(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static CommandResult AttachVerificationToCommandResult(
        CommandResult result,
        VerificationStatus verificationStatus,
        string verificationReason)
    {
        return new CommandResult
        {
            Status = result.Status,
            Safety = result.Safety,
            RiskLevel = result.RiskLevel,
            Decision = result.Decision,
            SelectedTool = result.SelectedTool,
            ToolExecutionResult = result.ToolExecutionResult,
            Message = result.Message,
            VerificationStatus = verificationStatus,
            VerificationReason = verificationReason,
            PendingApprovalSnapshot = result.PendingApprovalSnapshot,
            RuntimeId = result.RuntimeId,
            RuntimeStepCount = result.RuntimeStepCount,
            RuntimeTerminalState = result.RuntimeTerminalState,
            DecisionCycleState = result.DecisionCycleState
        };
    }

    private static DecisionExecutionOutcome NormalizeCommandResultOutcome(CommandResult result)
    {
        return result.Status switch
        {
            "Accepted" => DecisionExecutionOutcome.Succeeded,
            "Denied" => string.IsNullOrWhiteSpace(result.Message)
                ? DecisionExecutionOutcome.Failed
                : result.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                    ? DecisionExecutionOutcome.Blocked
                    : DecisionExecutionOutcome.Failed,
            "PendingApproval" => DecisionExecutionOutcome.Blocked,
            _ => DecisionExecutionOutcome.NotExecuted
        };
    }

    private static ExecutionRuntimeState BuildRuntimeStateFromFeedback(DecisionExecutionFeedback feedback)
    {
        var actionStatus = feedback.NormalizedOutcome switch
        {
            DecisionExecutionOutcome.Succeeded => ExecutionStatus.Succeeded,
            DecisionExecutionOutcome.Failed => ExecutionStatus.Failed,
            DecisionExecutionOutcome.Blocked => ExecutionStatus.Blocked,
            DecisionExecutionOutcome.NotExecuted => ExecutionStatus.Skipped,
            _ => ExecutionStatus.Planned
        };

        return new ExecutionRuntimeState
        {
            CapabilityName = "DecisionLoop",
            ActionName = feedback.ExecutedActionSummary,
            ActionStatus = actionStatus,
            VerificationStatus = feedback.Verification?.Status,
            VerificationReason = feedback.Verification?.Reason ?? feedback.Verification?.Summary,
            TargetKind = null,
            TargetValue = feedback.ProducedTarget,
            BlockedReason = feedback.BlockedReason,
            FailureReason = feedback.ExecutionFailed ? feedback.ExecutionMessage : null,
            PrimitiveKind = null,
            PrimitiveSucceeded = feedback.ExecutionSucceeded
        };
    }

    private static DecisionExecutionFeedback BuildDecisionExecutionFeedback(
        NextActionDecision? decision,
        CommandResult result)
    {
        var actionType = decision?.ExecuteAction?.ActionType;
        var actionTarget = decision?.ExecuteAction?.Target.Reference;
        var verificationPolicy = ActionVerificationPolicyResolver.Resolve(actionType);
        var actionSummary = actionType is null
            ? "no-action"
            : string.IsNullOrWhiteSpace(actionTarget)
                ? actionType.Value.ToString()
                : $"{actionType.Value}:{actionTarget}";

        var normalizedOutcome = NormalizeCommandResultOutcome(result);
        var verificationStatus = result.VerificationStatus;

        var verificationOutcome = DetermineVerificationOutcome(
            verificationPolicy,
            verificationStatus,
            normalizedOutcome);

        var failureCategory = normalizedOutcome switch
        {
            DecisionExecutionOutcome.Succeeded => DecisionFailureCategory.None,
            DecisionExecutionOutcome.Blocked when result.Status == "PendingApproval" => DecisionFailureCategory.BlockedByApproval,
            DecisionExecutionOutcome.Blocked => DecisionFailureCategory.BlockedByPolicy,
            DecisionExecutionOutcome.Failed => DecisionFailureCategory.ExecutionFailed,
            DecisionExecutionOutcome.NotExecuted when decision?.Kind == DecisionKind.AskObserve => DecisionFailureCategory.ObservationRequired,
            DecisionExecutionOutcome.NotExecuted => DecisionFailureCategory.InvalidDecision,
            _ => DecisionFailureCategory.Unknown
        };

        var producedResult = string.Equals(result.ToolExecutionResult, "Not executed", StringComparison.OrdinalIgnoreCase)
            ? null
            : result.ToolExecutionResult;

        var verificationReason = result.VerificationReason;
        if (string.IsNullOrWhiteSpace(verificationReason))
        {
            verificationReason = BuildDefaultVerificationReason(verificationPolicy, verificationOutcome, normalizedOutcome);
        }

        bool? targetReached = verificationOutcome switch
        {
            DecisionVerificationOutcome.VerifiedSuccess => true,
            DecisionVerificationOutcome.VerifiedFailure => false,
            DecisionVerificationOutcome.Inconclusive => false,
            DecisionVerificationOutcome.Unsupported => false,
            _ => normalizedOutcome == DecisionExecutionOutcome.NotExecuted
                ? null
                : normalizedOutcome == DecisionExecutionOutcome.Succeeded
                    ? true
                    : false
        };

        bool? retryableFailure = verificationOutcome == DecisionVerificationOutcome.Inconclusive
            ? verificationPolicy.InconclusiveRetryable
            : verificationOutcome is DecisionVerificationOutcome.VerifiedFailure or DecisionVerificationOutcome.Unsupported
                ? false
                : null;

        return new DecisionExecutionFeedback
        {
            ExecutedActionSummary = actionSummary,
            ExecutionSucceeded = normalizedOutcome == DecisionExecutionOutcome.Succeeded,
            ExecutionFailed = normalizedOutcome == DecisionExecutionOutcome.Failed,
            ExecutionBlocked = normalizedOutcome == DecisionExecutionOutcome.Blocked,
            ExecutionMessage = result.Message,
            NormalizedOutcome = normalizedOutcome,
            ProducedResult = producedResult,
            ProducedTarget = actionTarget,
            FailureCategory = failureCategory,
            BlockedReason = normalizedOutcome == DecisionExecutionOutcome.Blocked
                ? result.Message
                : null,
            Verification = new DecisionVerificationSummary
            {
                Status = verificationStatus,
                Kind = verificationPolicy.VerificationKind,
                Outcome = verificationOutcome,
                Summary = verificationReason,
                Reason = verificationReason,
                TargetReached = targetReached,
                TargetContext = actionTarget,
                EvaluatedAtUtc = DateTimeOffset.UtcNow,
                RetryableFailure = retryableFailure
            },
            Approval = new DecisionApprovalSummary
            {
                ApprovalRequired = result.Status == "PendingApproval",
                ApprovalPath = false,
                Approved = null,
                Reason = result.Status == "PendingApproval" ? result.Message : null
            }
        };
    }

    private static DecisionLoopPostExecutionTransition DeterminePostExecutionTransition(
        DecisionCycleRuntimeState runtimeState,
        NextActionDecision nextActionDecision,
        DecisionExecutionFeedback feedback,
        ExecutionRuntimeState? observationRuntimeState,
        CommandObservationSnapshot? richObservation)
    {
        var hasExplicitContinueSignal = HasExplicitContinueSignal(nextActionDecision);
        var verificationOutcome = feedback.Verification?.Outcome ?? DecisionVerificationOutcome.Unknown;
        var verificationRetryable = feedback.Verification?.RetryableFailure == true;
        var observationAssessment = AssessObservationRuntimeState(observationRuntimeState, feedback);
        var observationEnvelopeTransition = TryCreateObservationEnvelopeTransition(nextActionDecision, richObservation);
        if (observationEnvelopeTransition is not null)
        {
            return ApplyObservationRuntimeTransitionPolicy(
                runtimeState,
                hasExplicitContinueSignal,
                observationAssessment,
                observationEnvelopeTransition,
                "observation_envelope_guard");
        }

        if (verificationOutcome == DecisionVerificationOutcome.Unsupported)
        {
            if (IsHardStopFeedback(feedback))
            {
                return ApplyObservationRuntimeTransitionPolicy(
                    runtimeState,
                    hasExplicitContinueSignal,
                    observationAssessment,
                    CreateTerminalTransition(
                    DecisionLoopTerminalState.Aborted,
                    DecisionRuntimeOutcome.Aborted,
                    DecisionRuntimeBlockedState.Blocked,
                    DecisionRuntimeTerminationReason.HardStop),
                    "verification_unsupported_hard_stop");
            }

            return ApplyObservationRuntimeTransitionPolicy(
                runtimeState,
                hasExplicitContinueSignal,
                observationAssessment,
                CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.NonRetryableFailure),
                "verification_unsupported");
        }

        if (verificationOutcome == DecisionVerificationOutcome.Inconclusive)
        {
            if (IsHardStopFeedback(feedback))
            {
                return ApplyObservationRuntimeTransitionPolicy(
                    runtimeState,
                    hasExplicitContinueSignal,
                    observationAssessment,
                    CreateTerminalTransition(
                    DecisionLoopTerminalState.Aborted,
                    DecisionRuntimeOutcome.Aborted,
                    DecisionRuntimeBlockedState.Blocked,
                    DecisionRuntimeTerminationReason.HardStop),
                    "verification_inconclusive_hard_stop");
            }

            if (verificationRetryable && hasExplicitContinueSignal)
            {
                return ApplyObservationRuntimeTransitionPolicy(
                    runtimeState,
                    hasExplicitContinueSignal,
                    observationAssessment,
                    CreateRetryableTransitionOrLimit(runtimeState),
                    "verification_inconclusive_retryable");
            }

            return ApplyObservationRuntimeTransitionPolicy(
                runtimeState,
                hasExplicitContinueSignal,
                observationAssessment,
                CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.NonRetryableFailure),
                "verification_inconclusive");
        }

        if (verificationOutcome == DecisionVerificationOutcome.VerifiedFailure)
        {
            if (IsHardStopFeedback(feedback))
            {
                return ApplyObservationRuntimeTransitionPolicy(
                    runtimeState,
                    hasExplicitContinueSignal,
                    observationAssessment,
                    CreateTerminalTransition(
                    DecisionLoopTerminalState.Aborted,
                    DecisionRuntimeOutcome.Aborted,
                    DecisionRuntimeBlockedState.Blocked,
                    DecisionRuntimeTerminationReason.HardStop),
                    "verification_failed_hard_stop");
            }

            if (hasExplicitContinueSignal)
            {
                return ApplyObservationRuntimeTransitionPolicy(
                    runtimeState,
                    hasExplicitContinueSignal,
                    observationAssessment,
                    CreateRetryableTransitionOrLimit(runtimeState),
                    "verification_failed_retryable");
            }

            return ApplyObservationRuntimeTransitionPolicy(
                runtimeState,
                hasExplicitContinueSignal,
                observationAssessment,
                CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.NonRetryableFailure),
                "verification_failed");
        }

        if (feedback.ExecutionSucceeded)
        {
            var baseTransition = hasExplicitContinueSignal
                ? CreateContinueTransition(DecisionRuntimeOutcome.InProgress, DecisionRuntimeBlockedState.None)
                : CreateTerminalTransition(
                    DecisionLoopTerminalState.Completed,
                    DecisionRuntimeOutcome.Completed,
                    DecisionRuntimeBlockedState.None,
                    DecisionRuntimeTerminationReason.GoalCompleted);
            return ApplyObservationRuntimeTransitionPolicy(
                runtimeState,
                hasExplicitContinueSignal,
                observationAssessment,
                baseTransition,
                "execution_succeeded");
        }

        if (feedback.ExecutionBlocked)
        {
            if (IsHardStopFeedback(feedback))
            {
                return ApplyObservationRuntimeTransitionPolicy(
                    runtimeState,
                    hasExplicitContinueSignal,
                    observationAssessment,
                    CreateTerminalTransition(
                    DecisionLoopTerminalState.Aborted,
                    DecisionRuntimeOutcome.Aborted,
                    DecisionRuntimeBlockedState.Blocked,
                    DecisionRuntimeTerminationReason.HardStop),
                    "execution_blocked_hard_stop");
            }

            return ApplyObservationRuntimeTransitionPolicy(
                runtimeState,
                hasExplicitContinueSignal,
                observationAssessment,
                CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.NonRetryableFailure),
                "execution_blocked");
        }

        if (feedback.ExecutionFailed)
        {
            if (IsHardStopFeedback(feedback))
            {
                return ApplyObservationRuntimeTransitionPolicy(
                    runtimeState,
                    hasExplicitContinueSignal,
                    observationAssessment,
                    CreateTerminalTransition(
                    DecisionLoopTerminalState.Aborted,
                    DecisionRuntimeOutcome.Aborted,
                    DecisionRuntimeBlockedState.Blocked,
                    DecisionRuntimeTerminationReason.HardStop),
                    "execution_failed_hard_stop");
            }

            if (hasExplicitContinueSignal)
            {
                return ApplyObservationRuntimeTransitionPolicy(
                    runtimeState,
                    hasExplicitContinueSignal,
                    observationAssessment,
                    CreateRetryableTransitionOrLimit(runtimeState),
                    "execution_failed_retryable");
            }

            return ApplyObservationRuntimeTransitionPolicy(
                runtimeState,
                hasExplicitContinueSignal,
                observationAssessment,
                CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.NonRetryableFailure),
                "execution_failed");
        }

        if (feedback.NormalizedOutcome == DecisionExecutionOutcome.NotExecuted && hasExplicitContinueSignal)
        {
            return ApplyObservationRuntimeTransitionPolicy(
                runtimeState,
                hasExplicitContinueSignal,
                observationAssessment,
                CreateContinueTransition(DecisionRuntimeOutcome.InProgress, DecisionRuntimeBlockedState.None),
                "not_executed_continue");
        }

        return ApplyObservationRuntimeTransitionPolicy(
            runtimeState,
            hasExplicitContinueSignal,
            observationAssessment,
            CreateTerminalTransition(
            DecisionLoopTerminalState.Blocked,
            DecisionRuntimeOutcome.Blocked,
            DecisionRuntimeBlockedState.Blocked,
            DecisionRuntimeTerminationReason.NonRetryableFailure),
            "default_terminal");
    }

    private static DecisionLoopPostExecutionTransition? TryCreateObservationEnvelopeTransition(
        NextActionDecision nextActionDecision,
        CommandObservationSnapshot? richObservation)
    {
        if (richObservation is null)
        {
            return null;
        }

        if (richObservation.CollectionStatus == ObservationCollectionStatus.Failed)
        {
            return CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.NonRetryableFailure);
        }

        if (richObservation.CollectionStatus == ObservationCollectionStatus.Partial &&
            !string.IsNullOrWhiteSpace(richObservation.CollectionMessage) &&
            (richObservation.CollectionMessage.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
             richObservation.CollectionMessage.Contains("failed", StringComparison.OrdinalIgnoreCase)))
        {
            return CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.NonRetryableFailure);
        }

        if (richObservation.SafetySummary?.RequiresApproval == true)
        {
            return CreateTerminalTransition(
                DecisionLoopTerminalState.AwaitingApproval,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.AwaitingApproval);
        }

        if (nextActionDecision.ExecuteAction is not null &&
            nextActionDecision.ExecuteAction.ActionType is ActionType.Launch or ActionType.OpenFile &&
            richObservation.GroundingSummary is not null &&
            richObservation.GroundingSummary.Disposition is TargetGroundingDisposition.Unresolved
                or TargetGroundingDisposition.Unsupported
                or TargetGroundingDisposition.Ambiguous)
        {
            return CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.Blocked,
                DecisionRuntimeTerminationReason.NonRetryableFailure);
        }

        return null;
    }

    private static ObservationRuntimeAssessment AssessObservationRuntimeState(
        ExecutionRuntimeState? observationRuntimeState,
        DecisionExecutionFeedback feedback)
    {
        if (observationRuntimeState is null)
        {
            return ObservationRuntimeAssessment.None;
        }

        var hasBlockedSignal = !string.IsNullOrWhiteSpace(observationRuntimeState.BlockedReason) ||
                               observationRuntimeState.ActionStatus == ExecutionStatus.Blocked;
        var hasFailureSignal = !string.IsNullOrWhiteSpace(observationRuntimeState.FailureReason) ||
                               observationRuntimeState.ActionStatus is ExecutionStatus.Failed or ExecutionStatus.VerificationFailed;
        var hasSuccessSignal = observationRuntimeState.ActionStatus is ExecutionStatus.Succeeded or ExecutionStatus.PartiallySucceeded;
        var verificationSuccessful = observationRuntimeState.VerificationStatus == VerificationStatus.Verified;
        var verificationFailed = observationRuntimeState.VerificationStatus is VerificationStatus.NotVerified
            or VerificationStatus.Inconclusive
            or VerificationStatus.Unsupported;

        var conflictType = string.Empty;
        if (feedback.ExecutionSucceeded && (hasBlockedSignal || hasFailureSignal || verificationFailed))
        {
            conflictType = "feedback_success_vs_observation_failure";
        }
        else if ((feedback.ExecutionBlocked || feedback.ExecutionFailed) && hasSuccessSignal && verificationSuccessful)
        {
            conflictType = "feedback_failure_vs_observation_success";
        }

        return new ObservationRuntimeAssessment
        {
            Consumed = true,
            HasBlockedSignal = hasBlockedSignal,
            HasFailureSignal = hasFailureSignal,
            HasSuccessSignal = hasSuccessSignal,
            VerificationSuccessful = verificationSuccessful,
            VerificationFailed = verificationFailed,
            ConflictType = conflictType,
            HasConflict = !string.IsNullOrWhiteSpace(conflictType)
        };
    }

    private static DecisionLoopPostExecutionTransition ApplyObservationRuntimeTransitionPolicy(
        DecisionCycleRuntimeState runtimeState,
        bool hasExplicitContinueSignal,
        ObservationRuntimeAssessment assessment,
        DecisionLoopPostExecutionTransition baseTransition,
        string decisionBasis)
    {
        var transition = baseTransition;
        var policyMode = "authoritative_cycle";
        var overrideApplied = false;

        if (assessment.Consumed)
        {
            // Mixed-policy override: observation runtime state can override only on strong conflicts.
            if (transition.ContinueLoop && (assessment.HasBlockedSignal || assessment.HasFailureSignal || assessment.VerificationFailed))
            {
                transition = CreateRetryableTransitionOrLimit(runtimeState);
                policyMode = "observation_override";
                overrideApplied = true;
            }
            else if (transition.BlockedState == DecisionRuntimeBlockedState.Retrying &&
                     assessment.HasSuccessSignal &&
                     assessment.VerificationSuccessful)
            {
                transition = hasExplicitContinueSignal
                    ? CreateContinueTransition(DecisionRuntimeOutcome.InProgress, DecisionRuntimeBlockedState.None)
                    : CreateTerminalTransition(
                        DecisionLoopTerminalState.Completed,
                        DecisionRuntimeOutcome.Completed,
                        DecisionRuntimeBlockedState.None,
                        DecisionRuntimeTerminationReason.GoalCompleted);
                policyMode = "observation_override";
                overrideApplied = true;
            }
        }

        return new DecisionLoopPostExecutionTransition
        {
            ContinueLoop = transition.ContinueLoop,
            Transition = transition.Transition,
            TerminalState = transition.TerminalState,
            BlockedState = transition.BlockedState,
            Outcome = transition.Outcome,
            TerminationReason = transition.TerminationReason,
            IncrementRetryCount = transition.IncrementRetryCount,
            ObservationRuntimeConsumed = assessment.Consumed,
            ObservationRuntimeConflict = assessment.HasConflict,
            ObservationRuntimeConflictType = assessment.ConflictType,
            ObservationRuntimeOverrideApplied = overrideApplied,
            ObservationRuntimePolicyMode = policyMode,
            ObservationRuntimeDecisionBasis = decisionBasis
        };
    }

    private static DecisionLoopPostExecutionTransition CreateRetryableTransitionOrLimit(DecisionCycleRuntimeState runtimeState)
    {
        var nextRetryCount = runtimeState.RetryCount + 1;
        if (nextRetryCount > runtimeState.MaxRetryLimit)
        {
            return CreateTerminalTransition(
                DecisionLoopTerminalState.Blocked,
                DecisionRuntimeOutcome.Blocked,
                DecisionRuntimeBlockedState.RetryLimitReached,
                DecisionRuntimeTerminationReason.RetryLimitReached);
        }

        return CreateContinueTransition(
            DecisionRuntimeOutcome.RetryableFailure,
            DecisionRuntimeBlockedState.Retrying,
            incrementRetryCount: true);
    }

    private static DecisionVerificationOutcome DetermineVerificationOutcome(
        ActionVerificationPolicy policy,
        VerificationStatus? verificationStatus,
        DecisionExecutionOutcome normalizedOutcome)
    {
        if (!policy.RequiresVerification)
        {
            return DecisionVerificationOutcome.Unknown;
        }

        if (verificationStatus.HasValue)
        {
            return verificationStatus.Value switch
            {
                VerificationStatus.Verified => DecisionVerificationOutcome.VerifiedSuccess,
                VerificationStatus.NotVerified => DecisionVerificationOutcome.VerifiedFailure,
                VerificationStatus.Inconclusive => DecisionVerificationOutcome.Inconclusive,
                VerificationStatus.Unsupported => DecisionVerificationOutcome.Unsupported,
                _ => DecisionVerificationOutcome.Unknown
            };
        }

        if (policy.Mode == ActionVerificationMode.ExecutionDerived)
        {
            return DecisionVerificationOutcome.Unsupported;
        }

        if (policy.Mode == ActionVerificationMode.StrictExternal)
        {
            return DecisionVerificationOutcome.Unsupported;
        }

        return DecisionVerificationOutcome.Unknown;
    }

    private static string BuildDefaultVerificationReason(
        ActionVerificationPolicy policy,
        DecisionVerificationOutcome verificationOutcome,
        DecisionExecutionOutcome normalizedOutcome)
    {
        if (verificationOutcome == DecisionVerificationOutcome.VerifiedSuccess)
        {
            return policy.Mode == ActionVerificationMode.ExecutionDerived
                ? "Execution-derived verification marked the action as successful."
                : "Verification primitives marked the action as successful.";
        }

        if (verificationOutcome == DecisionVerificationOutcome.VerifiedFailure)
        {
            return policy.Mode == ActionVerificationMode.ExecutionDerived
                ? "Execution-derived verification marked the action as failed."
                : "Verification primitives marked the action as failed.";
        }

        if (verificationOutcome == DecisionVerificationOutcome.Inconclusive)
        {
            return "Verification is inconclusive because post-action evidence is insufficient.";
        }

        if (verificationOutcome == DecisionVerificationOutcome.Unsupported)
        {
            return "Verification is unsupported for the current action evidence.";
        }

        return normalizedOutcome == DecisionExecutionOutcome.NotExecuted
            ? "Step did not execute an action."
            : normalizedOutcome == DecisionExecutionOutcome.Succeeded
                ? "Step outcome indicates target reached."
                : "Step outcome indicates target not reached.";
    }

    private static bool HasExplicitContinueSignal(NextActionDecision decision)
    {
        return (TryReadDecisionMetadataBoolean(decision, ChainContinueMetadataKey, out var chainContinue) && chainContinue) ||
               (TryReadDecisionMetadataBoolean(decision, GoalPendingMetadataKey, out var goalPending) && goalPending);
    }

    private static bool IsHardStopFeedback(DecisionExecutionFeedback feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback.ExecutionMessage))
        {
            return false;
        }

        return feedback.ExecutionMessage.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
               feedback.ExecutionMessage.Contains("fail-closed", StringComparison.OrdinalIgnoreCase) ||
               feedback.ExecutionMessage.Contains("policy denied", StringComparison.OrdinalIgnoreCase) ||
               feedback.ExecutionMessage.Contains("approval rejected", StringComparison.OrdinalIgnoreCase) ||
               feedback.ExecutionMessage.Contains("safety denied", StringComparison.OrdinalIgnoreCase) ||
               feedback.ExecutionMessage.Contains("aborted", StringComparison.OrdinalIgnoreCase);
    }

    private static DecisionLoopPostExecutionTransition CreateContinueTransition(
        DecisionRuntimeOutcome outcome,
        DecisionRuntimeBlockedState blockedState,
        bool incrementRetryCount = false)
    {
        return new DecisionLoopPostExecutionTransition
        {
            ContinueLoop = true,
            Transition = DecisionLoopTransition.Continue,
            TerminalState = DecisionLoopTerminalState.None,
            BlockedState = blockedState,
            Outcome = outcome,
            TerminationReason = DecisionRuntimeTerminationReason.None,
            IncrementRetryCount = incrementRetryCount
        };
    }

    private static DecisionLoopPostExecutionTransition CreateTerminalTransition(
        DecisionLoopTerminalState terminalState,
        DecisionRuntimeOutcome outcome,
        DecisionRuntimeBlockedState blockedState,
        DecisionRuntimeTerminationReason terminationReason)
    {
        return new DecisionLoopPostExecutionTransition
        {
            ContinueLoop = false,
            Transition = DecisionLoopTransition.Terminal,
            TerminalState = terminalState,
            BlockedState = blockedState,
            Outcome = outcome,
            TerminationReason = terminationReason,
            IncrementRetryCount = false
        };
    }

    private static bool TryReadDecisionMetadataBoolean(
        NextActionDecision decision,
        string key,
        out bool value)
    {
        value = false;

        return decision.Metadata is not null &&
               decision.Metadata.TryGetValue(key, out var rawValue) &&
               bool.TryParse(rawValue, out value);
    }

    private static bool TryResolveRetryAction(
        NextActionDecision retryDecision,
        ExecuteActionPayload? previousAction,
        out ExecuteActionPayload retryAction,
        out string reason)
    {
        reason = retryDecision.RetryReason ?? retryDecision.Retry?.RetryReason ?? "Retry requested.";

        if (retryDecision.Retry?.Action is not null)
        {
            retryAction = retryDecision.Retry.Action;
            return true;
        }

        if (previousAction is not null)
        {
            retryAction = previousAction;
            return true;
        }

        retryAction = new ExecuteActionPayload();
        reason = "Retry requested but no executable action is available.";
        return false;
    }

    private static DecisionLoopTerminalState MapStopDispositionToTerminalState(StopDisposition disposition)
    {
        return disposition switch
        {
            StopDisposition.Completed => DecisionLoopTerminalState.Completed,
            StopDisposition.Aborted => DecisionLoopTerminalState.Aborted,
            _ => DecisionLoopTerminalState.Blocked
        };
    }

    private static CommandResult BuildLoopTerminalResult(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        string status,
        string selectedTool,
        string decisionText,
        string toolExecutionResult,
        string message,
        DecisionCycleRuntimeState runtimeState)
    {
        UpdateRuntimeSessionCompletion(runtimeState);

        return new CommandResult
        {
            Status = status,
            Safety = safety.ToString(),
            RiskLevel = riskLevel.ToString(),
            Decision = decisionText,
            SelectedTool = selectedTool,
            ToolExecutionResult = toolExecutionResult,
            Message = message,
            VerificationStatus = null,
            VerificationReason = null,
            PendingApprovalSnapshot = null,
            RuntimeId = runtimeState.RuntimeId,
            RuntimeStepCount = runtimeState.CurrentStepIndex,
            RuntimeTerminalState = runtimeState.TerminalState,
            DecisionCycleState = runtimeState
        };
    }

    private static CommandResult AttachRuntimeState(CommandResult result, DecisionCycleRuntimeState runtimeState)
    {
        UpdateRuntimeSessionCompletion(runtimeState);
        var pendingSnapshot = AttachRuntimeState(result.PendingApprovalSnapshot, runtimeState);

        return new CommandResult
        {
            Status = result.Status,
            Safety = result.Safety,
            RiskLevel = result.RiskLevel,
            Decision = result.Decision,
            SelectedTool = result.SelectedTool,
            ToolExecutionResult = result.ToolExecutionResult,
            Message = result.Message,
            VerificationStatus = result.VerificationStatus,
            VerificationReason = result.VerificationReason,
            PendingApprovalSnapshot = pendingSnapshot,
            RuntimeId = runtimeState.RuntimeId,
            RuntimeStepCount = runtimeState.CurrentStepIndex,
            RuntimeTerminalState = runtimeState.TerminalState,
            DecisionCycleState = runtimeState
        };
    }

    private static PendingApprovalSnapshot? AttachRuntimeState(
        PendingApprovalSnapshot? snapshot,
        DecisionCycleRuntimeState runtimeState)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new PendingApprovalSnapshot
        {
            CorrelationId = snapshot.CorrelationId,
            CommandText = snapshot.CommandText,
            Decision = snapshot.Decision,
            PendingDecisionContract = snapshot.PendingDecisionContract,
            ApprovedDecisionContract = snapshot.ApprovedDecisionContract,
            ResolvedTargets = snapshot.ResolvedTargets,
            ContextAdapter = snapshot.ContextAdapter,
            ObservationSummary = snapshot.ObservationSummary,
            RuntimeState = runtimeState,
            PendingStepApprovalKey = snapshot.PendingStepApprovalKey,
            ApprovedStepApprovalKey = snapshot.ApprovedStepApprovalKey,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            Metadata = snapshot.Metadata
        };
    }

    private static DecisionCycleRuntimeState CreateInitialDecisionCycleRuntimeState(
        CommandRequest request,
        DecisionInputSource source,
        int maxStepLimit,
        int maxRetryLimit)
    {
        var now = DateTimeOffset.UtcNow;

        return new DecisionCycleRuntimeState
        {
            RuntimeId = request.CorrelationId,
            OriginalUserRequest = request.UserInput,
            CurrentStepIndex = 0,
            MaxStepLimit = maxStepLimit,
            CurrentDecision = null,
            LastExecutableAction = null,
            LastExecutionFeedback = null,
            LastVerificationSummary = null,
            LastVerificationStatus = null,
            LastVerificationKind = DecisionVerificationKind.Unknown,
            LastVerificationOutcome = DecisionVerificationOutcome.Unknown,
            LastVerificationReason = null,
            LastVerificationTarget = null,
            LastVerificationAtUtc = null,
            LastObservationSummary = source == DecisionInputSource.ApprovedSnapshot
                ? "resumed-from-approved-snapshot"
                : "live-start",
            AwaitingApproval = false,
            CompletionState = DecisionRuntimeCompletionState.Running,
            TerminalState = DecisionLoopTerminalState.None,
            BlockedState = DecisionRuntimeBlockedState.None,
            RetryCount = 0,
            MaxRetryLimit = maxRetryLimit,
            GoalStillActive = true,
            Outcome = DecisionRuntimeOutcome.InProgress,
            TerminationReason = DecisionRuntimeTerminationReason.None,
            StepHistorySummary = "",
            StepHistory = [],
            RuntimeSession = new RuntimeSessionState
            {
                RuntimeSessionId = request.CorrelationId,
                OriginalCommand = request.UserInput,
                CurrentGoal = new RuntimeGoalState
                {
                    GoalId = request.CorrelationId,
                    Description = request.UserInput
                },
                CurrentStep = null,
                LastStepResult = null,
                RetryCount = 0,
                BlockedCount = 0,
                CompletionState = RuntimeSessionCompletionState.InProgress,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }
        };
    }

    private static void UpdateRuntimeVerificationState(
        DecisionCycleRuntimeState runtimeState,
        DecisionVerificationSummary? verification,
        string? fallbackTarget)
    {
        runtimeState.LastVerificationSummary = verification?.Summary;
        runtimeState.LastVerificationStatus = verification?.Status;
        runtimeState.LastVerificationKind = verification?.Kind ?? DecisionVerificationKind.Unknown;
        runtimeState.LastVerificationOutcome = verification?.Outcome ?? DecisionVerificationOutcome.Unknown;
        runtimeState.LastVerificationReason = verification?.Reason ?? verification?.Summary;
        runtimeState.LastVerificationTarget = string.IsNullOrWhiteSpace(verification?.TargetContext)
            ? fallbackTarget
            : verification.TargetContext;
        runtimeState.LastVerificationAtUtc = verification?.EvaluatedAtUtc;
    }

    private static void AppendDecisionCycleStep(
        DecisionCycleRuntimeState runtimeState,
        DecisionKind decisionKind,
        ActionType? actionType,
        string? targetSummary,
        DecisionExecutionOutcome outcome,
        DecisionLoopTransition transition,
        DecisionLoopTerminalState terminalState,
        string? reason)
    {
        var history = runtimeState.StepHistory.ToList();
        history.Add(new DecisionCycleStepRecord
        {
            StepIndex = runtimeState.CurrentStepIndex,
            DecisionKind = decisionKind,
            ActionType = actionType,
            TargetSummary = targetSummary,
            ExecutionOutcome = outcome,
            Transition = transition,
            TerminalState = terminalState,
            Reason = reason
        });

        runtimeState.StepHistory = history;
        runtimeState.StepHistorySummary = string.Join(" | ", history.Select(step =>
            $"#{step.StepIndex}:{step.DecisionKind}/{step.ExecutionOutcome}/{step.Transition}"));

        runtimeState.RuntimeSession.CurrentStep = new RuntimeStepState
        {
            StepIndex = runtimeState.CurrentStepIndex,
            DecisionKind = decisionKind,
            ActionType = actionType,
            TargetReference = targetSummary,
            Summary = reason
        };

        var isBlocked = outcome == DecisionExecutionOutcome.Blocked;
        if (isBlocked)
        {
            runtimeState.RuntimeSession.BlockedCount++;
        }

        runtimeState.RuntimeSession.LastStepResult = new RuntimeStepResultState
        {
            Outcome = outcome,
            Succeeded = outcome == DecisionExecutionOutcome.Succeeded,
            Failed = outcome == DecisionExecutionOutcome.Failed,
            Blocked = isBlocked,
            Message = reason,
            ProducedTarget = targetSummary
        };

        runtimeState.RuntimeSession.RetryCount = runtimeState.RetryCount;
        runtimeState.RuntimeSession.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static Dictionary<string, string> BuildDecisionLoopFlags(
        DecisionCycleRuntimeState runtimeState,
        IDictionary<string, string>? contextMetadata = null)
    {
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeId"] = runtimeState.RuntimeId,
            ["runtimeStepIndex"] = runtimeState.CurrentStepIndex.ToString(),
            ["runtimeStepLimit"] = runtimeState.MaxStepLimit.ToString(),
            ["runtimeGoalStillActive"] = runtimeState.GoalStillActive ? "true" : "false",
            ["runtimeAwaitingApproval"] = runtimeState.AwaitingApproval ? "true" : "false",
            ["runtimeCompletionState"] = runtimeState.CompletionState.ToString(),
            ["runtimeTerminalState"] = runtimeState.TerminalState.ToString(),
            ["runtimeBlockedState"] = runtimeState.BlockedState.ToString(),
            ["runtimeOutcome"] = runtimeState.Outcome.ToString(),
            ["runtimeTerminationReason"] = runtimeState.TerminationReason.ToString(),
            ["runtimeRetryCount"] = runtimeState.RetryCount.ToString(),
            ["runtimeRetryLimit"] = runtimeState.MaxRetryLimit.ToString(),
            ["runtimeHistorySummary"] = runtimeState.StepHistorySummary,
            ["runtimeSessionId"] = runtimeState.RuntimeSession.RuntimeSessionId,
            ["runtimeSessionCompletionState"] = runtimeState.RuntimeSession.CompletionState.ToString(),
            ["runtimeSessionRetryCount"] = runtimeState.RuntimeSession.RetryCount.ToString(),
            ["runtimeSessionBlockedCount"] = runtimeState.RuntimeSession.BlockedCount.ToString()
        };

        if (!string.IsNullOrWhiteSpace(runtimeState.RuntimeSession.OriginalCommand))
        {
            flags["runtimeOriginalCommand"] = runtimeState.RuntimeSession.OriginalCommand;
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.RuntimeSession.CurrentGoal.Description))
        {
            flags["runtimeCurrentGoal"] = runtimeState.RuntimeSession.CurrentGoal.Description;
        }

        if (runtimeState.RuntimeSession.CurrentStep is not null)
        {
            flags["runtimeCurrentStepIndex"] = runtimeState.RuntimeSession.CurrentStep.StepIndex.ToString();
            flags["runtimeCurrentStepDecisionKind"] = runtimeState.RuntimeSession.CurrentStep.DecisionKind.ToString();

            if (runtimeState.RuntimeSession.CurrentStep.ActionType is ActionType currentActionType)
            {
                flags["runtimeCurrentStepActionType"] = currentActionType.ToString();
            }

            if (!string.IsNullOrWhiteSpace(runtimeState.RuntimeSession.CurrentStep.TargetReference))
            {
                flags["runtimeCurrentStepTarget"] = runtimeState.RuntimeSession.CurrentStep.TargetReference;
            }
        }

        if (runtimeState.RuntimeSession.LastStepResult is not null)
        {
            flags["runtimeLastStepOutcome"] = runtimeState.RuntimeSession.LastStepResult.Outcome.ToString();
            flags["runtimeLastStepSucceeded"] = runtimeState.RuntimeSession.LastStepResult.Succeeded ? "true" : "false";
            flags["runtimeLastStepFailed"] = runtimeState.RuntimeSession.LastStepResult.Failed ? "true" : "false";
            flags["runtimeLastStepBlocked"] = runtimeState.RuntimeSession.LastStepResult.Blocked ? "true" : "false";

            if (!string.IsNullOrWhiteSpace(runtimeState.RuntimeSession.LastStepResult.Message))
            {
                flags["runtimeLastStepMessage"] = runtimeState.RuntimeSession.LastStepResult.Message;
            }
        }

        if (runtimeState.CurrentDecision is not null)
        {
            flags["runtimeCurrentDecisionKind"] = runtimeState.CurrentDecision.Kind.ToString();

            if (runtimeState.CurrentDecision.ExecuteAction is not null)
            {
                flags["runtimeCurrentActionType"] = runtimeState.CurrentDecision.ExecuteAction.ActionType.ToString();

                if (!string.IsNullOrWhiteSpace(runtimeState.CurrentDecision.ExecuteAction.Target.Reference))
                {
                    flags["runtimeCurrentActionTarget"] = runtimeState.CurrentDecision.ExecuteAction.Target.Reference;
                }
            }
        }

        if (runtimeState.LastExecutionFeedback is not null)
        {
            flags["runtimeLastExecutionOutcome"] = runtimeState.LastExecutionFeedback.NormalizedOutcome.ToString();
            flags["runtimeLastExecutionMessage"] = runtimeState.LastExecutionFeedback.ExecutionMessage;
            flags["runtimeLastExecutionSucceeded"] = runtimeState.LastExecutionFeedback.ExecutionSucceeded ? "true" : "false";
            flags["runtimeLastExecutionFailed"] = runtimeState.LastExecutionFeedback.ExecutionFailed ? "true" : "false";
            flags["runtimeLastExecutionBlocked"] = runtimeState.LastExecutionFeedback.ExecutionBlocked ? "true" : "false";
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.LastVerificationSummary))
        {
            flags["runtimeLastVerificationSummary"] = runtimeState.LastVerificationSummary;
        }

        if (runtimeState.LastVerificationStatus.HasValue)
        {
            flags["runtimeLastVerificationStatus"] = runtimeState.LastVerificationStatus.Value.ToString();
        }

        if (runtimeState.LastVerificationKind != DecisionVerificationKind.Unknown)
        {
            flags["runtimeLastVerificationKind"] = runtimeState.LastVerificationKind.ToString();
        }

        if (runtimeState.LastVerificationOutcome != DecisionVerificationOutcome.Unknown)
        {
            flags["runtimeLastVerificationOutcome"] = runtimeState.LastVerificationOutcome.ToString();
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.LastVerificationReason))
        {
            flags["runtimeLastVerificationReason"] = runtimeState.LastVerificationReason;
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.LastVerificationTarget))
        {
            flags["runtimeLastVerificationTarget"] = runtimeState.LastVerificationTarget;
        }

        if (runtimeState.LastVerificationAtUtc.HasValue)
        {
            flags["runtimeLastVerificationAtUtc"] = runtimeState.LastVerificationAtUtc.Value.ToString("O");
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.LastObservationSummary))
        {
            flags["runtimeLastObservationSummary"] = runtimeState.LastObservationSummary;
        }

        if (contextMetadata is not null)
        {
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_policy_mode");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_conflict_detected");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_override_applied");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_routing_score_basis");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_runtime_consumed");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_runtime_conflict");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_runtime_override_applied");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_runtime_policy_mode");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_runtime_conflict_type");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_runtime_decision_basis");
            CopyContextFlagIfPresent(contextMetadata, flags, "approval_resume_live_observation_captured");
            CopyContextFlagIfPresent(contextMetadata, flags, "approval_resume_observation_drift_detected");
            CopyContextFlagIfPresent(contextMetadata, flags, "approval_resume_target_aligned");
            CopyContextFlagIfPresent(contextMetadata, flags, "approval_resume_keep_approved_step");
            CopyContextFlagIfPresent(contextMetadata, flags, "approval_resume_observation_policy_mode");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_role_operational_fields");
            CopyContextFlagIfPresent(contextMetadata, flags, "observation_role_descriptive_fields");
        }

        return flags;
    }

    private static void CopyContextFlagIfPresent(
        IDictionary<string, string> source,
        IDictionary<string, string> destination,
        string key)
    {
        if (source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            destination[key] = value;
        }
    }

    private static void UpdateRuntimeSessionCurrentStep(
        DecisionCycleRuntimeState runtimeState,
        NextActionDecision? decision)
    {
        var action = decision?.ExecuteAction ?? decision?.AskApproval?.ProposedAction ?? decision?.Retry?.Action;

        runtimeState.RuntimeSession.CurrentStep = new RuntimeStepState
        {
            StepIndex = runtimeState.CurrentStepIndex,
            DecisionKind = decision?.Kind ?? DecisionKind.Stop,
            ActionType = action?.ActionType,
            TargetReference = action?.Target.Reference,
            Summary = decision?.Message
        };

        runtimeState.RuntimeSession.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void UpdateRuntimeSessionCompletion(DecisionCycleRuntimeState runtimeState)
    {
        runtimeState.RuntimeSession.RetryCount = runtimeState.RetryCount;
        runtimeState.RuntimeSession.CompletionState = runtimeState.TerminalState switch
        {
            DecisionLoopTerminalState.None => RuntimeSessionCompletionState.InProgress,
            DecisionLoopTerminalState.AwaitingApproval => RuntimeSessionCompletionState.InProgress,
            DecisionLoopTerminalState.Completed => RuntimeSessionCompletionState.Completed,
            DecisionLoopTerminalState.Aborted => RuntimeSessionCompletionState.Aborted,
            _ => RuntimeSessionCompletionState.Blocked
        };
        runtimeState.RuntimeSession.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task LogStepSubmittedAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        AgentExecutionContext? context,
        AiDecision? decision,
        DecisionCycleRuntimeState runtimeState,
        string selectedTool,
        string executionMode,
        string approvalDecision,
        CancellationToken cancellationToken,
        string executionPath = "decision-loop")
    {
        var metadata = CreateExecutionPathMetadata(executionPath);
        if (decision is not null)
        {
            EnrichMetadataWithNextActionDecision(metadata, decision);
        }

        foreach (var (key, value) in BuildDecisionLoopFlags(runtimeState))
        {
            metadata[key] = value;
        }

        var submittedStep = runtimeState.StepHistory.LastOrDefault();
        if (submittedStep is not null)
        {
            metadata["submitted_step_index"] = submittedStep.StepIndex.ToString();
            metadata["submitted_step_decision_kind"] = submittedStep.DecisionKind.ToString();
            metadata["submitted_step_outcome"] = submittedStep.ExecutionOutcome.ToString();
            metadata["submitted_step_transition"] = submittedStep.Transition.ToString();
            metadata["submitted_step_terminal_state"] = submittedStep.TerminalState.ToString();

            if (submittedStep.ActionType is ActionType actionType)
            {
                metadata["submitted_step_action_type"] = actionType.ToString();
            }

            if (!string.IsNullOrWhiteSpace(submittedStep.TargetSummary))
            {
                metadata["submitted_step_target"] = submittedStep.TargetSummary;
            }

            if (!string.IsNullOrWhiteSpace(submittedStep.Reason))
            {
                metadata["submitted_step_reason"] = submittedStep.Reason;
            }
        }

        var submittedStepDedupKey = BuildSubmittedStepDedupKey(runtimeState, decision);
        if (!string.IsNullOrWhiteSpace(submittedStepDedupKey))
        {
            var correlationCacheKey = BuildVerificationOutcomeCacheKey(request.CorrelationId, context?.CorrelationId);
            lock (_submittedStepEventByCorrelationId)
            {
                if (_submittedStepEventByCorrelationId.TryGetValue(correlationCacheKey, out var existingDedupKey) &&
                    existingDedupKey.Equals(submittedStepDedupKey, StringComparison.Ordinal))
                {
                    return;
                }

                _submittedStepEventByCorrelationId[correlationCacheKey] = submittedStepDedupKey;
            }
        }

        await LogEventAsync(
            request,
            "StepSubmitted",
            safety.ToString(),
            riskLevel.ToString(),
            selectedTool,
            executionMode,
            "Submitted",
            runtimeState.RuntimeSession.CurrentStep?.Summary ?? "Runtime step submitted.",
            approvalDecision,
            cancellationToken,
            context,
            metadata);
    }

    private async Task LogDecisionLoopStepAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        AgentExecutionContext context,
        AiDecision decision,
        DecisionCycleRuntimeState runtimeState,
        DecisionLoopTransition transition,
        string message,
        string outcome,
        string selectedTool,
        string executionMode,
        CancellationToken cancellationToken)
    {
        var metadata = CreateExecutionPathMetadata("decision-loop");
        EnrichMetadataWithNextActionDecision(metadata, decision);

        foreach (var (key, value) in BuildDecisionLoopFlags(runtimeState))
        {
            metadata[key] = value;
        }

        metadata["loop_transition"] = transition.ToString();
        metadata["loop_outcome"] = outcome;

        await LogStepSubmittedAsync(
            request,
            safety,
            riskLevel,
            context,
            decision,
            runtimeState,
            selectedTool,
            executionMode,
            string.Empty,
            cancellationToken);

        await LogEventAsync(
            request,
            "DecisionLoopStep",
            safety.ToString(),
            riskLevel.ToString(),
            selectedTool,
            executionMode,
            outcome,
            message,
            string.Empty,
            cancellationToken,
            context,
            metadata);
    }

    private async Task LogDecisionLoopVerificationAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        AgentExecutionContext context,
        AiDecision decision,
        DecisionCycleRuntimeState runtimeState,
        DecisionExecutionFeedback feedback,
        string selectedTool,
        string executionMode,
        CancellationToken cancellationToken)
    {
        var metadata = CreateExecutionPathMetadata("decision-loop");
        EnrichMetadataWithNextActionDecision(metadata, decision);

        foreach (var (key, value) in BuildDecisionLoopFlags(runtimeState))
        {
            metadata[key] = value;
        }

        metadata["verification_outcome"] = feedback.NormalizedOutcome.ToString();

        if (feedback.Verification?.Status is VerificationStatus verificationStatus)
        {
            metadata["verification_status"] = verificationStatus.ToString();
        }

        if (feedback.Verification is not null)
        {
            metadata["verification_kind"] = feedback.Verification.Kind.ToString();
            metadata["verification_result_outcome"] = feedback.Verification.Outcome.ToString();

            if (feedback.Verification.RetryableFailure.HasValue)
            {
                metadata["verification_retryable_failure"] = feedback.Verification.RetryableFailure.Value
                    ? "true"
                    : "false";
            }

            if (!string.IsNullOrWhiteSpace(feedback.Verification.TargetContext))
            {
                metadata["verification_target_context"] = feedback.Verification.TargetContext;
            }

            if (feedback.Verification.EvaluatedAtUtc.HasValue)
            {
                metadata["verification_evaluated_at_utc"] = feedback.Verification.EvaluatedAtUtc.Value.ToString("O");
            }
        }

        if (!string.IsNullOrWhiteSpace(feedback.Verification?.Summary))
        {
            metadata["verification_summary"] = feedback.Verification!.Summary!;
        }

        metadata["verification_target_reached"] = feedback.Verification?.TargetReached switch
        {
            true => "true",
            false => "false",
            _ => "unknown"
        };

        var verificationOutcomeCacheKey = BuildVerificationOutcomeCacheKey(request.CorrelationId, context.CorrelationId);
        lock (_verificationOutcomeByCorrelationId)
        {
            _verificationOutcomeByCorrelationId[verificationOutcomeCacheKey] =
                feedback.Verification?.Outcome.ToString() ?? DecisionVerificationOutcome.Unknown.ToString();
        }

        await LogEventAsync(
            request,
            "DecisionLoopVerification",
            safety.ToString(),
            riskLevel.ToString(),
            string.IsNullOrWhiteSpace(selectedTool) ? "None" : selectedTool,
            executionMode,
            feedback.NormalizedOutcome.ToString(),
            feedback.Verification?.Summary ?? feedback.ExecutionMessage,
            string.Empty,
            cancellationToken,
            context,
            metadata);

        if (string.Equals(executionMode, "not-executed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var verificationMetadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["step_verified"] = "true",
            ["step_verified_source_event"] = "DecisionLoopVerification"
        };

        await LogEventAsync(
            request,
            "StepVerified",
            safety.ToString(),
            riskLevel.ToString(),
            string.IsNullOrWhiteSpace(selectedTool) ? "None" : selectedTool,
            executionMode,
            feedback.Verification?.Outcome.ToString() ?? DecisionVerificationOutcome.Unknown.ToString(),
            feedback.Verification?.Summary ?? feedback.ExecutionMessage,
            string.Empty,
            cancellationToken,
            context,
            verificationMetadata);
    }

    private sealed class DecisionLoopExecutionResult
    {
        public CommandResult CommandResult { get; init; } = new();
        public DecisionExecutionFeedback Feedback { get; init; } = new();
    }

    private sealed class MetaDecisionHandlingResult
    {
        public AiDecision Decision { get; init; } = new();
        public CommandResult? Result { get; init; }
    }

    private sealed class StepSafetyInterceptionResult
    {
        public CommandResult? Result { get; init; }
        public bool ConsumeApprovedStepKey { get; init; }
    }

    private sealed class DecisionLoopPostExecutionTransition
    {
        public bool ContinueLoop { get; init; }
        public DecisionLoopTransition Transition { get; init; }
        public DecisionLoopTerminalState TerminalState { get; init; }
        public DecisionRuntimeBlockedState BlockedState { get; init; }
        public DecisionRuntimeOutcome Outcome { get; init; }
        public DecisionRuntimeTerminationReason TerminationReason { get; init; }
        public bool IncrementRetryCount { get; init; }
        public bool ObservationRuntimeConsumed { get; init; }
        public bool ObservationRuntimeConflict { get; init; }
        public string? ObservationRuntimeConflictType { get; init; }
        public bool ObservationRuntimeOverrideApplied { get; init; }
        public string ObservationRuntimePolicyMode { get; init; } = "authoritative_cycle";
        public string? ObservationRuntimeDecisionBasis { get; init; }
    }

    private sealed class ObservationRuntimeAssessment
    {
        public static readonly ObservationRuntimeAssessment None = new();

        public bool Consumed { get; init; }
        public bool HasBlockedSignal { get; init; }
        public bool HasFailureSignal { get; init; }
        public bool HasSuccessSignal { get; init; }
        public bool VerificationSuccessful { get; init; }
        public bool VerificationFailed { get; init; }
        public bool HasConflict { get; init; }
        public string ConflictType { get; init; } = string.Empty;
    }

    private sealed class ApprovalResumeObservationAssessment
    {
        public AgentExecutionContext Context { get; init; } = default!;
        public bool LiveObservationCaptured { get; init; }
        public bool DriftDetected { get; init; }
        public bool TargetAligned { get; init; }
        public bool KeepApprovedDecision { get; init; } = true;
        public string PolicyMode { get; init; } = "snapshot-authoritative";
    }

    public async Task<CommandResult> ExecuteApprovedAsync(
        CommandRequest request,
        SafetyRiskLevel riskLevel,
        PendingApprovalSnapshot? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        var snapshotUsed = false;
        var snapshotTargetReasonNormalized = false;
        string? snapshotTargetReasonSource = null;
        var snapshotAdapterPreserved = false;
        var snapshotObservationPreserved = false;
        var approvedStepApprovalKey = ResolveApprovedStepApprovalKey(snapshot);
        string? approvalResumeFailureReason = null;

        AgentExecutionContext executionContext;
        AiDecision decision;

        if (TryCreateApprovedExecutionStateFromSnapshot(
                request,
                snapshot,
                riskLevel,
                out executionContext,
                out decision,
                out snapshotTargetReasonNormalized,
                out snapshotAdapterPreserved,
                out snapshotObservationPreserved,
                out approvalResumeFailureReason))
        {
            snapshotUsed = true;
            snapshotTargetReasonSource = snapshotTargetReasonNormalized ? "normalization" : "snapshot";

            var requiresGroundingRecovery = executionContext.PrimaryTargetGrounding is null &&
                                            HasOnlyUnknownTargetReasons(executionContext.ResolvedTargets) &&
                                            executionContext.ResolvedTargets.All(target => target.Kind == TargetKind.Application);
            if (executionContext.ResolvedTargets.Count == 0 || requiresGroundingRecovery)
            {
                executionContext = await EnsurePrimaryTargetGroundingAsync(request, executionContext, cancellationToken);
            }
        }
        else
        {
            return await BuildApprovalResumeFailedResultAsync(
                request,
                riskLevel,
                snapshot,
                approvalResumeFailureReason ?? "invalid_contract",
                cancellationToken);
        }

        decision = NormalizeDecision(decision);
        executionContext = ApplyDetectedIntent(executionContext, decision);

        var approvalResumeObservation = await ApplyApprovalResumeObservationPolicyAsync(
            request,
            executionContext,
            decision,
            snapshot,
            riskLevel,
            cancellationToken);
        executionContext = approvalResumeObservation.Context;

        var runtimeState = snapshot!.RuntimeState!;

        runtimeState.RuntimeId = request.CorrelationId;
        runtimeState.OriginalUserRequest = request.UserInput;
        runtimeState.MaxStepLimit = Math.Max(
            runtimeState.MaxStepLimit,
            Math.Max(runtimeState.CurrentStepIndex + 1, DefaultDecisionLoopMaxSteps));
        runtimeState.MaxRetryLimit = DefaultDecisionRetryLimit;
        runtimeState.AwaitingApproval = false;
        runtimeState.CompletionState = DecisionRuntimeCompletionState.Running;
        runtimeState.TerminalState = DecisionLoopTerminalState.None;
        runtimeState.BlockedState = DecisionRuntimeBlockedState.None;
        runtimeState.GoalStillActive = true;
        runtimeState.Outcome = DecisionRuntimeOutcome.InProgress;
        runtimeState.TerminationReason = DecisionRuntimeTerminationReason.None;
        runtimeState.RuntimeSession.RuntimeSessionId = runtimeState.RuntimeId;
        runtimeState.RuntimeSession.OriginalCommand = runtimeState.OriginalUserRequest;
        runtimeState.RuntimeSession.RetryCount = runtimeState.RetryCount;
        runtimeState.RuntimeSession.CompletionState = RuntimeSessionCompletionState.InProgress;
        runtimeState.RuntimeSession.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(runtimeState.RuntimeSession.CurrentGoal.Description))
        {
            runtimeState.RuntimeSession.CurrentGoal = new RuntimeGoalState
            {
                GoalId = runtimeState.RuntimeId,
                Description = runtimeState.OriginalUserRequest
            };
        }

        var selectedTool = ResolveDecisionToolNameForDisplay(decision);
        if (string.IsNullOrWhiteSpace(selectedTool))
        {
            selectedTool = "None";
        }

        await LogApprovalDecisionAuditAsync(
            request,
            riskLevel,
            selectedTool,
            approvalDecision: "Approved",
            message: "User approved pending command.",
            cancellationToken,
            runtimeState,
            executionPath: "approval-resume",
            pendingStepApprovalKey: snapshot?.PendingStepApprovalKey,
            approvedStepApprovalKey,
            decisionSource: "orchestrator",
            resumeFromApproval: true);

        return await RunDecisionLoopAsync(
            request,
            SafetyDisposition.RequiresApproval,
            riskLevel,
            approvalPath: true,
            executionContext,
            runtimeState,
            decisionInputSource: snapshotUsed ? DecisionInputSource.ApprovedSnapshot : DecisionInputSource.Live,
            snapshotUsed,
            snapshotFallback: false,
            snapshotUsed ? snapshotTargetReasonNormalized : null,
            snapshotTargetReasonSource,
            snapshotUsed ? snapshotAdapterPreserved || snapshotObservationPreserved : null,
            snapshotUsed ? snapshotAdapterPreserved : null,
            snapshotUsed ? snapshotObservationPreserved : null,
            initialDecision: approvalResumeObservation.KeepApprovedDecision ? decision : null,
            approvedStepApprovalKey,
            resumePendingStep: true,
            cancellationToken: cancellationToken);
    }

    private async Task<ApprovalResumeObservationAssessment> ApplyApprovalResumeObservationPolicyAsync(
        CommandRequest request,
        AgentExecutionContext context,
        AiDecision approvedDecision,
        PendingApprovalSnapshot? snapshot,
        SafetyRiskLevel riskLevel,
        CancellationToken cancellationToken)
    {
        ObservationSnapshot? liveObservation = null;
        var liveObservationCaptured = false;
        try
        {
            liveObservation = await _observationProvider.CaptureAsync(cancellationToken);
            liveObservationCaptured = liveObservation is not null;
        }
        catch
        {
            // Live observation refresh is best-effort for approval resume.
        }

        var decisionTargetReference = approvedDecision.NextActionDecision?.ExecuteAction?.Target.Reference;
        if (!liveObservationCaptured)
        {
            return new ApprovalResumeObservationAssessment
            {
                Context = EnrichApprovalResumeObservationMetadata(
                    context,
                    liveCaptured: false,
                    driftDetected: false,
                    targetAligned: true,
                    keepApprovedDecision: true,
                    policyMode: "snapshot-authoritative"),
                LiveObservationCaptured = false,
                DriftDetected = false,
                TargetAligned = true,
                KeepApprovedDecision = true,
                PolicyMode = "snapshot-authoritative"
            };
        }

        var liveSummary = BuildPendingObservationSummary(liveObservation);
        var snapshotSummary = snapshot?.ObservationSummary;
        var driftDetected = HasPendingObservationDrift(snapshotSummary, liveSummary);
        var targetAligned = MatchesTargetHint(liveObservation, decisionTargetReference);
        var keepApprovedDecision = !driftDetected || targetAligned;
        var policyMode = keepApprovedDecision
            ? "snapshot-live-aligned"
            : "snapshot-drift-replan";

        var refreshedRichObservation = await CaptureCommandObservationAsync(
            new CommandObservationRequest
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                OriginalUserCommand = request.UserInput,
                BaselineObservation = liveObservation,
                PrimaryTargetGrounding = context.PrimaryTargetGrounding,
                SafetyDisposition = SafetyDisposition.RequiresApproval,
                SafetyRiskLevel = riskLevel
            },
            cancellationToken);

        var refreshedContext = new AgentExecutionContext
        {
            CorrelationId = context.CorrelationId,
            RawInput = context.RawInput,
            NormalizedInput = context.NormalizedInput,
            DetectedIntent = context.DetectedIntent,
            CreatedAtUtc = context.CreatedAtUtc,
            SessionId = context.SessionId,
            Observation = liveObservation,
            RichObservation = refreshedRichObservation,
            ContextAdapter = context.ContextAdapter,
            PrimaryTargetGrounding = context.PrimaryTargetGrounding,
            RuntimeState = context.RuntimeState,
            ResolvedTargets = context.ResolvedTargets,
            Metadata = context.Metadata
        };

        return new ApprovalResumeObservationAssessment
        {
            Context = EnrichApprovalResumeObservationMetadata(
                refreshedContext,
                liveCaptured: true,
                driftDetected,
                targetAligned,
                keepApprovedDecision,
                policyMode),
            LiveObservationCaptured = true,
            DriftDetected = driftDetected,
            TargetAligned = targetAligned,
            KeepApprovedDecision = keepApprovedDecision,
            PolicyMode = policyMode
        };
    }

    private static bool HasPendingObservationDrift(
        PendingObservationSummary? snapshotSummary,
        PendingObservationSummary? liveSummary)
    {
        if (snapshotSummary is null || liveSummary is null)
        {
            return false;
        }

        var snapshotProcess = NormalizeProcessNameForComparison(snapshotSummary.ActiveProcessName);
        var liveProcess = NormalizeProcessNameForComparison(liveSummary.ActiveProcessName);
        if (!StringsEqual(snapshotProcess, liveProcess))
        {
            return true;
        }

        if (snapshotSummary.ActiveWindowHandle.HasValue &&
            liveSummary.ActiveWindowHandle.HasValue &&
            snapshotSummary.ActiveWindowHandle.Value != liveSummary.ActiveWindowHandle.Value)
        {
            return true;
        }

        if (!StringsEqual(snapshotSummary.ActiveWindowTitle, liveSummary.ActiveWindowTitle))
        {
            return true;
        }

        return false;
    }

    private static AgentExecutionContext EnrichApprovalResumeObservationMetadata(
        AgentExecutionContext context,
        bool liveCaptured,
        bool driftDetected,
        bool targetAligned,
        bool keepApprovedDecision,
        string policyMode)
    {
        var metadata = context.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(context.Metadata, StringComparer.OrdinalIgnoreCase);

        metadata["approval_resume_live_observation_captured"] = liveCaptured ? "true" : "false";
        metadata["approval_resume_observation_drift_detected"] = driftDetected ? "true" : "false";
        metadata["approval_resume_target_aligned"] = targetAligned ? "true" : "false";
        metadata["approval_resume_keep_approved_step"] = keepApprovedDecision ? "true" : "false";
        metadata["approval_resume_observation_policy_mode"] = policyMode;

        return new AgentExecutionContext
        {
            CorrelationId = context.CorrelationId,
            RawInput = context.RawInput,
            NormalizedInput = context.NormalizedInput,
            DetectedIntent = context.DetectedIntent,
            CreatedAtUtc = context.CreatedAtUtc,
            SessionId = context.SessionId,
            Observation = context.Observation,
            RichObservation = context.RichObservation,
            ContextAdapter = context.ContextAdapter,
            PrimaryTargetGrounding = context.PrimaryTargetGrounding,
            RuntimeState = context.RuntimeState,
            ResolvedTargets = context.ResolvedTargets,
            Metadata = metadata
        };
    }

    public async Task<CommandResult> RejectPendingAsync(
        CommandRequest request,
        SafetyRiskLevel riskLevel,
        PendingApprovalSnapshot? snapshot = null,
        string fallbackSelectedTool = "None",
        CancellationToken cancellationToken = default)
    {
        var result = BuildRejectedApprovalResult(riskLevel, snapshot, fallbackSelectedTool);
        var runtimeState = result.DecisionCycleState ?? snapshot?.RuntimeState;
        var approvedStepApprovalKey = ResolveApprovedStepApprovalKey(snapshot);

        await LogApprovalDecisionAuditAsync(
            request,
            riskLevel,
            result.SelectedTool,
            approvalDecision: "Rejected",
            message: "User rejected pending command.",
            cancellationToken,
            runtimeState,
            executionPath: "approval-reject",
            pendingStepApprovalKey: snapshot?.PendingStepApprovalKey,
            approvedStepApprovalKey,
            decisionSource: "orchestrator",
            haltReason: "approval_rejected",
            resumeFromApproval: false);

        var metadata = CreateExecutionPathMetadata("approval-reject");
        EnrichApprovalMetadata(
            metadata,
            stepIndex: runtimeState?.CurrentStepIndex,
            pendingStepApprovalKey: snapshot?.PendingStepApprovalKey,
            approvedStepApprovalKey,
            decisionSource: "orchestrator",
            haltReason: "approval_rejected",
            resumeFromApproval: false);

        if (runtimeState is not null)
        {
            foreach (var (key, value) in BuildDecisionLoopFlags(runtimeState))
            {
                metadata[key] = value;
            }
        }

        await LogEventAsync(
            request,
            "CommandCompleted",
            result.Safety,
            result.RiskLevel,
            result.SelectedTool,
            "not-executed",
            result.Status,
            result.Message,
            "Rejected",
            cancellationToken,
            null,
            metadata);

        return result;
    }

    private async Task<CommandResult> BuildApprovalResumeFailedResultAsync(
        CommandRequest request,
        SafetyRiskLevel riskLevel,
        PendingApprovalSnapshot? snapshot,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var message = $"Pending step could not be resumed ({failureReason}). Approval was not executed.";
        var decision = snapshot is null ? null : BuildDecisionForApprovedSnapshot(snapshot);
        var decisionText = !string.IsNullOrWhiteSpace(decision?.DecisionText)
            ? decision.DecisionText
            : "Pending approval resume failed.";
        var selectedTool = decision is null
            ? "None"
            : ResolveDecisionToolNameForDisplay(decision);

        if (string.IsNullOrWhiteSpace(selectedTool))
        {
            selectedTool = "None";
        }

        var runtimeState = snapshot?.RuntimeState ?? CreateInitialDecisionCycleRuntimeState(
            request,
            DecisionInputSource.ApprovedSnapshot,
            maxStepLimit: DefaultDecisionLoopMaxSteps,
            maxRetryLimit: DefaultDecisionRetryLimit);

        runtimeState.RuntimeId = request.CorrelationId;
        runtimeState.OriginalUserRequest = request.UserInput;
        runtimeState.AwaitingApproval = false;
        runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
        runtimeState.TerminalState = DecisionLoopTerminalState.Blocked;
        runtimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
        runtimeState.GoalStillActive = false;
        runtimeState.Outcome = DecisionRuntimeOutcome.Blocked;
        runtimeState.TerminationReason = DecisionRuntimeTerminationReason.NonRetryableFailure;
        runtimeState.LastExecutionFeedback = new DecisionExecutionFeedback
        {
            ExecutedActionSummary = "approval-resume-failed",
            ExecutionSucceeded = false,
            ExecutionFailed = false,
            ExecutionBlocked = true,
            ExecutionMessage = message,
            NormalizedOutcome = DecisionExecutionOutcome.Blocked,
            ProducedResult = null,
            ProducedTarget = decision?.NextActionDecision?.ExecuteAction?.Target.Reference,
            FailureCategory = DecisionFailureCategory.BlockedByPolicy,
            BlockedReason = message,
            Verification = new DecisionVerificationSummary
            {
                Status = null,
                Summary = "Pending approval resume failed.",
                Reason = message,
                TargetReached = false
            },
            Approval = new DecisionApprovalSummary
            {
                ApprovalRequired = true,
                ApprovalPath = false,
                Approved = true,
                Reason = message
            }
        };

        UpdateRuntimeVerificationState(
            runtimeState,
            runtimeState.LastExecutionFeedback.Verification,
            runtimeState.LastExecutionFeedback.ProducedTarget);
        runtimeState.LastObservationSummary = message;
        runtimeState.RuntimeSession.RuntimeSessionId = runtimeState.RuntimeId;
        runtimeState.RuntimeSession.OriginalCommand = runtimeState.OriginalUserRequest;
        runtimeState.RuntimeSession.RetryCount = runtimeState.RetryCount;
        runtimeState.RuntimeSession.CompletionState = RuntimeSessionCompletionState.Blocked;
        runtimeState.RuntimeSession.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(runtimeState.RuntimeSession.CurrentGoal.Description))
        {
            runtimeState.RuntimeSession.CurrentGoal = new RuntimeGoalState
            {
                GoalId = runtimeState.RuntimeId,
                Description = runtimeState.OriginalUserRequest
            };
        }

        AppendDecisionCycleStep(
            runtimeState,
            decisionKind: DecisionKind.Stop,
            actionType: null,
            targetSummary: null,
            outcome: DecisionExecutionOutcome.Blocked,
            transition: DecisionLoopTransition.Terminal,
            terminalState: runtimeState.TerminalState,
            reason: message);

        await LogStepSubmittedAsync(
            request,
            SafetyDisposition.Denied,
            riskLevel,
            null,
            decision,
            runtimeState,
            selectedTool,
            executionMode: "not-executed",
            approvalDecision: "Approved",
            cancellationToken,
            executionPath: "approval-resume");

        var result = new CommandResult
        {
            Status = "Denied",
            Safety = SafetyDisposition.Denied.ToString(),
            RiskLevel = riskLevel.ToString(),
            Decision = decisionText,
            SelectedTool = selectedTool,
            ToolExecutionResult = "Not executed",
            Message = message,
            PendingApprovalSnapshot = null,
            RuntimeId = runtimeState.RuntimeId,
            RuntimeStepCount = runtimeState.CurrentStepIndex,
            RuntimeTerminalState = runtimeState.TerminalState,
            DecisionCycleState = runtimeState
        };

        var metadata = CreateExecutionPathMetadata(
            "approval-resume",
            snapshotUsed: false,
            snapshotFallback: false);
        metadata["approval_resume_mode"] = "fail_closed";
        metadata["approval_resume_reason"] = failureReason;

        await LogApprovalDecisionAuditAsync(
            request,
            riskLevel,
            result.SelectedTool,
            approvalDecision: "Approved",
            message: "User approved pending command, but resume failed.",
            cancellationToken,
            runtimeState,
            executionPath: "approval-resume",
            pendingStepApprovalKey: snapshot?.PendingStepApprovalKey,
            approvedStepApprovalKey: ResolveApprovedStepApprovalKey(snapshot),
            decisionSource: "orchestrator",
            haltReason: "approval_resume_failed",
            resumeFromApproval: true);

        await LogEventAsync(
            request,
            "ApprovalResumeFailed",
            result.Safety,
            result.RiskLevel,
            result.SelectedTool,
            "not-executed",
            result.Status,
            result.Message,
            "Approved",
            cancellationToken,
            null,
            metadata);

        await LogEventAsync(
            request,
            "CommandCompleted",
            result.Safety,
            result.RiskLevel,
            result.SelectedTool,
            "not-executed",
            result.Status,
            result.Message,
            "Approved",
            cancellationToken,
            null,
            metadata);

        return result;
    }

    private async Task LogApprovalDecisionAuditAsync(
        CommandRequest request,
        SafetyRiskLevel riskLevel,
        string selectedTool,
        string approvalDecision,
        string message,
        CancellationToken cancellationToken,
        DecisionCycleRuntimeState? runtimeState,
        string executionPath,
        string? pendingStepApprovalKey,
        string? approvedStepApprovalKey,
        string decisionSource,
        string? haltReason = null,
        bool? resumeFromApproval = null)
    {
        var metadata = CreateExecutionPathMetadata(executionPath);
        EnrichApprovalMetadata(
            metadata,
            runtimeState?.CurrentStepIndex,
            pendingStepApprovalKey,
            approvedStepApprovalKey,
            decisionSource,
            haltReason,
            resumeFromApproval);

        if (runtimeState is not null)
        {
            foreach (var (key, value) in BuildDecisionLoopFlags(runtimeState))
            {
                metadata[key] = value;
            }
        }

        await LogEventAsync(
            request,
            "ApprovalDecision",
            SafetyDisposition.RequiresApproval.ToString(),
            riskLevel.ToString(),
            string.IsNullOrWhiteSpace(selectedTool) ? "None" : selectedTool,
            "not-executed",
            approvalDecision,
            message,
            approvalDecision,
            cancellationToken,
            null,
            metadata);
    }

    private static void EnrichApprovalMetadata(
        IDictionary<string, string> metadata,
        int? stepIndex,
        string? pendingStepApprovalKey,
        string? approvedStepApprovalKey,
        string decisionSource,
        string? haltReason,
        bool? resumeFromApproval)
    {
        metadata["approval_decision_source"] = decisionSource;
        metadata["pending_step_approval_key_present"] = string.IsNullOrWhiteSpace(pendingStepApprovalKey) ? "false" : "true";
        metadata["approved_step_approval_key_present"] = string.IsNullOrWhiteSpace(approvedStepApprovalKey) ? "false" : "true";

        if (stepIndex.HasValue)
        {
            metadata["approval_step_index"] = stepIndex.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(haltReason))
        {
            metadata["haltReason"] = haltReason;
        }

        if (resumeFromApproval.HasValue)
        {
            metadata["resumeFromApproval"] = resumeFromApproval.Value ? "true" : "false";
        }
    }

    public static CommandResult BuildRejectedApprovalResult(
        SafetyRiskLevel riskLevel,
        PendingApprovalSnapshot? snapshot = null,
        string fallbackSelectedTool = "None")
    {
        var effectiveDecision = snapshot is null ? null : BuildDecisionForApprovedSnapshot(snapshot);
        var decisionText = effectiveDecision?.DecisionText;

        if (string.IsNullOrWhiteSpace(decisionText) && snapshot?.Decision.NextActionDecision is not null)
        {
            decisionText = BuildDecisionTextFromNextAction(snapshot.Decision.NextActionDecision);
        }

        if (string.IsNullOrWhiteSpace(decisionText))
        {
            decisionText = "Execution cancelled by user";
        }

        var selectedTool = effectiveDecision is null
            ? fallbackSelectedTool
            : ResolveDecisionToolNameForDisplay(effectiveDecision);

        if (string.IsNullOrWhiteSpace(selectedTool))
        {
            selectedTool = "None";
        }

        var runtimeState = CloneRuntimeStateForApprovalClosure(snapshot?.RuntimeState) ?? new DecisionCycleRuntimeState();
        runtimeState.RuntimeId = string.IsNullOrWhiteSpace(runtimeState.RuntimeId)
            ? snapshot?.CorrelationId ?? Guid.NewGuid().ToString("N")
            : runtimeState.RuntimeId;
        runtimeState.OriginalUserRequest = string.IsNullOrWhiteSpace(runtimeState.OriginalUserRequest)
            ? snapshot?.CommandText ?? string.Empty
            : runtimeState.OriginalUserRequest;
        runtimeState.MaxStepLimit = Math.Max(runtimeState.MaxStepLimit, Math.Max(runtimeState.CurrentStepIndex, 1));
        runtimeState.MaxRetryLimit = Math.Max(runtimeState.MaxRetryLimit, DefaultDecisionRetryLimit);

        runtimeState.AwaitingApproval = false;
        runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
        runtimeState.TerminalState = DecisionLoopTerminalState.Aborted;
        runtimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
        runtimeState.GoalStillActive = false;
        runtimeState.Outcome = DecisionRuntimeOutcome.Aborted;
        runtimeState.TerminationReason = DecisionRuntimeTerminationReason.ApprovalRejected;
        runtimeState.CurrentDecision = null;
        runtimeState.LastExecutableAction = null;
        runtimeState.LastExecutionFeedback = new DecisionExecutionFeedback
        {
            ExecutedActionSummary = "approval-rejected",
            ExecutionSucceeded = false,
            ExecutionFailed = false,
            ExecutionBlocked = true,
            ExecutionMessage = "User rejected the command.",
            NormalizedOutcome = DecisionExecutionOutcome.Blocked,
            ProducedResult = null,
            ProducedTarget = null,
            FailureCategory = DecisionFailureCategory.BlockedByApproval,
            BlockedReason = "User rejected the command.",
            Verification = new DecisionVerificationSummary
            {
                Status = null,
                Summary = "Execution stopped because approval was rejected.",
                Reason = "Execution stopped because approval was rejected.",
                TargetReached = false
            },
            Approval = new DecisionApprovalSummary
            {
                ApprovalRequired = true,
                ApprovalPath = false,
                Approved = false,
                Reason = "User rejected the command."
            }
        };
        UpdateRuntimeVerificationState(
            runtimeState,
            runtimeState.LastExecutionFeedback.Verification,
            runtimeState.LastExecutionFeedback.ProducedTarget);
        runtimeState.LastObservationSummary = "Approval request rejected by user.";

        runtimeState.RuntimeSession.RuntimeSessionId = runtimeState.RuntimeId;
        runtimeState.RuntimeSession.OriginalCommand = runtimeState.OriginalUserRequest;
        runtimeState.RuntimeSession.RetryCount = runtimeState.RetryCount;
        runtimeState.RuntimeSession.CompletionState = RuntimeSessionCompletionState.Aborted;
        runtimeState.RuntimeSession.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(runtimeState.RuntimeSession.CurrentGoal.Description))
        {
            runtimeState.RuntimeSession.CurrentGoal = new RuntimeGoalState
            {
                GoalId = runtimeState.RuntimeId,
                Description = runtimeState.OriginalUserRequest
            };
        }

        AppendDecisionCycleStep(
            runtimeState,
            decisionKind: DecisionKind.Stop,
            actionType: null,
            targetSummary: null,
            outcome: DecisionExecutionOutcome.Blocked,
            transition: DecisionLoopTransition.Terminal,
            terminalState: runtimeState.TerminalState,
            reason: "User rejected the command.");

        return new CommandResult
        {
            Status = "Rejected",
            Safety = SafetyDisposition.RequiresApproval.ToString(),
            RiskLevel = riskLevel.ToString(),
            Decision = decisionText,
            SelectedTool = selectedTool,
            ToolExecutionResult = "Not executed",
            Message = "User rejected the command.",
            PendingApprovalSnapshot = null,
            RuntimeId = runtimeState.RuntimeId,
            RuntimeStepCount = runtimeState.CurrentStepIndex,
            RuntimeTerminalState = runtimeState.TerminalState,
            DecisionCycleState = runtimeState
        };
    }

    private static DecisionCycleRuntimeState? CloneRuntimeStateForApprovalClosure(DecisionCycleRuntimeState? runtimeState)
    {
        if (runtimeState is null)
        {
            return null;
        }

        return new DecisionCycleRuntimeState
        {
            RuntimeId = runtimeState.RuntimeId,
            OriginalUserRequest = runtimeState.OriginalUserRequest,
            CurrentStepIndex = runtimeState.CurrentStepIndex,
            MaxStepLimit = runtimeState.MaxStepLimit,
            CurrentDecision = runtimeState.CurrentDecision,
            LastExecutableAction = runtimeState.LastExecutableAction,
            LastExecutionFeedback = runtimeState.LastExecutionFeedback,
            LastVerificationSummary = runtimeState.LastVerificationSummary,
            LastVerificationStatus = runtimeState.LastVerificationStatus,
            LastVerificationKind = runtimeState.LastVerificationKind,
            LastVerificationOutcome = runtimeState.LastVerificationOutcome,
            LastVerificationReason = runtimeState.LastVerificationReason,
            LastVerificationTarget = runtimeState.LastVerificationTarget,
            LastVerificationAtUtc = runtimeState.LastVerificationAtUtc,
            LastObservationSummary = runtimeState.LastObservationSummary,
            AwaitingApproval = runtimeState.AwaitingApproval,
            CompletionState = runtimeState.CompletionState,
            TerminalState = runtimeState.TerminalState,
            BlockedState = runtimeState.BlockedState,
            RetryCount = runtimeState.RetryCount,
            MaxRetryLimit = runtimeState.MaxRetryLimit,
            GoalStillActive = runtimeState.GoalStillActive,
            Outcome = runtimeState.Outcome,
            TerminationReason = runtimeState.TerminationReason,
            StepHistorySummary = runtimeState.StepHistorySummary,
            StepHistory = runtimeState.StepHistory.ToList(),
            RuntimeSession = new RuntimeSessionState
            {
                RuntimeSessionId = runtimeState.RuntimeSession.RuntimeSessionId,
                OriginalCommand = runtimeState.RuntimeSession.OriginalCommand,
                CurrentGoal = new RuntimeGoalState
                {
                    GoalId = runtimeState.RuntimeSession.CurrentGoal.GoalId,
                    Description = runtimeState.RuntimeSession.CurrentGoal.Description
                },
                CurrentStep = runtimeState.RuntimeSession.CurrentStep is null
                    ? null
                    : new RuntimeStepState
                    {
                        StepIndex = runtimeState.RuntimeSession.CurrentStep.StepIndex,
                        DecisionKind = runtimeState.RuntimeSession.CurrentStep.DecisionKind,
                        ActionType = runtimeState.RuntimeSession.CurrentStep.ActionType,
                        TargetReference = runtimeState.RuntimeSession.CurrentStep.TargetReference,
                        Summary = runtimeState.RuntimeSession.CurrentStep.Summary
                    },
                LastStepResult = runtimeState.RuntimeSession.LastStepResult is null
                    ? null
                    : new RuntimeStepResultState
                    {
                        Outcome = runtimeState.RuntimeSession.LastStepResult.Outcome,
                        Succeeded = runtimeState.RuntimeSession.LastStepResult.Succeeded,
                        Failed = runtimeState.RuntimeSession.LastStepResult.Failed,
                        Blocked = runtimeState.RuntimeSession.LastStepResult.Blocked,
                        Message = runtimeState.RuntimeSession.LastStepResult.Message,
                        ProducedTarget = runtimeState.RuntimeSession.LastStepResult.ProducedTarget
                    },
                RetryCount = runtimeState.RuntimeSession.RetryCount,
                BlockedCount = runtimeState.RuntimeSession.BlockedCount,
                CompletionState = runtimeState.RuntimeSession.CompletionState,
                CreatedAtUtc = runtimeState.RuntimeSession.CreatedAtUtc,
                UpdatedAtUtc = runtimeState.RuntimeSession.UpdatedAtUtc
            }
        };
    }

    private async Task<CommandResult> ExecuteToolPathAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        bool approvalPath,
        AgentExecutionContext? executionContext,
        CancellationToken cancellationToken,
        AiDecision? precomputedDecision = null,
        bool? snapshotUsed = null,
        bool? snapshotFallback = null,
        bool? targetReasonNormalized = null,
        string? targetReasonSource = null,
        bool? snapshotContextUsed = null,
        bool? snapshotAdapterPreserved = null,
        bool? snapshotObservationPreserved = null,
        DecisionInputBundle? decisionInputBundle = null,
        DecisionSummary? decisionSummary = null,
        AiDecisionInput? aiDecisionInput = null)
    {
        var effectiveAiDecisionInput = aiDecisionInput;
        if (effectiveAiDecisionInput is null)
        {
            var fallbackSummary = DecisionSummaryBuilder.Build(BuildRequestOnlyDecisionInputBundle(request, DecisionInputSource.Live));
            effectiveAiDecisionInput = AiDecisionInputBuilder.Build(request, fallbackSummary);
        }

        var decisionRequest = BuildAiDecisionRequest(
            request,
            effectiveAiDecisionInput,
            safetyDisposition: safety,
            safetyRiskLevel: riskLevel,
            approvalPath: approvalPath,
            snapshotUsed: snapshotUsed,
            snapshotFallback: snapshotFallback,
            executionContext: executionContext);
        var decision = NormalizeDecision(precomputedDecision ?? await _aiDecisionClient.DecideAsync(decisionRequest, cancellationToken));
        var toolPathMetadata = CreateExecutionPathMetadata(
            "tool",
            null,
            snapshotUsed,
            snapshotFallback,
            targetReasonNormalized,
            targetReasonSource,
            snapshotContextUsed,
            snapshotAdapterPreserved,
            snapshotObservationPreserved);
        EnrichMetadataWithDecisionInput(toolPathMetadata, decisionInputBundle);
        EnrichMetadataWithDecisionSummary(toolPathMetadata, decisionSummary);
        EnrichMetadataWithAiDecisionInput(toolPathMetadata, effectiveAiDecisionInput);
        EnrichMetadataWithNextActionDecision(toolPathMetadata, decision);

        var toolPathMetaDecision = await TryHandleMetaDecisionAsync(
            request,
            safety,
            riskLevel,
            approvalPath,
            executionContext,
            decision,
            cancellationToken,
            decisionInputBundle,
            decisionSummary,
            effectiveAiDecisionInput,
            toolPathMetadata,
            snapshotUsed,
            snapshotFallback,
            targetReasonNormalized,
            targetReasonSource,
            snapshotContextUsed,
            snapshotAdapterPreserved,
            snapshotObservationPreserved);

        decision = toolPathMetaDecision.Decision;

        if (toolPathMetaDecision.Result is not null)
        {
            return toolPathMetaDecision.Result;
        }

        var effectiveToolName = ResolveDecisionToolNameForExecution(decision);
        var effectiveToolArgument = ResolveDecisionToolArgumentForExecution(decision);

        if (string.IsNullOrWhiteSpace(effectiveToolName))
        {
            var noToolResult = new CommandResult
            {
                Status = "Accepted",
                Safety = safety.ToString(),
                RiskLevel = riskLevel.ToString(),
                Decision = decision.DecisionText,
                SelectedTool = "None",
                ToolExecutionResult = "Not executed",
                Message = "No matching tool for this command."
            };

            await LogEventAsync(request, "ExecutionBlocked", noToolResult.Safety, noToolResult.RiskLevel, noToolResult.SelectedTool, "not-executed", noToolResult.Status, noToolResult.Message, string.Empty, cancellationToken, executionContext, toolPathMetadata);
            await LogEventAsync(request, "CommandCompleted", noToolResult.Safety, noToolResult.RiskLevel, noToolResult.SelectedTool, "not-executed", noToolResult.Status, noToolResult.Message, string.Empty, cancellationToken, executionContext, toolPathMetadata);

            return noToolResult;
        }

        if (!_tools.TryGetValue(effectiveToolName, out var tool))
        {
            var unresolvedToolResult = new CommandResult
            {
                Status = "Accepted",
                Safety = safety.ToString(),
                RiskLevel = riskLevel.ToString(),
                Decision = decision.DecisionText,
                SelectedTool = effectiveToolName,
                ToolExecutionResult = "Not executed",
                Message = "Selected tool is not registered."
            };

            await LogEventAsync(request, "ExecutionBlocked", unresolvedToolResult.Safety, unresolvedToolResult.RiskLevel, unresolvedToolResult.SelectedTool, "not-executed", unresolvedToolResult.Status, unresolvedToolResult.Message, string.Empty, cancellationToken, executionContext, toolPathMetadata);
            await LogEventAsync(request, "CommandCompleted", unresolvedToolResult.Safety, unresolvedToolResult.RiskLevel, unresolvedToolResult.SelectedTool, "not-executed", unresolvedToolResult.Status, unresolvedToolResult.Message, string.Empty, cancellationToken, executionContext, toolPathMetadata);

            return unresolvedToolResult;
        }

        await LogEventAsync(request, "ToolResolved", safety.ToString(), riskLevel.ToString(), tool.Name, "not-executed", "Resolved", "Tool resolved successfully.", approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, toolPathMetadata);

        var toolRequest = new CommandRequest
        {
            CorrelationId = request.CorrelationId,
            UserInput = effectiveToolArgument ?? request.UserInput,
            RequestedAtUtc = request.RequestedAtUtc,
            PrimaryTargetGrounding = executionContext?.PrimaryTargetGrounding
        };

        var toolResult = await tool.ExecuteAsync(toolRequest, cancellationToken);
        EnrichMetadataWithPrimitiveExecution(toolPathMetadata, toolResult.PrimitiveExecution);
        var toolContext = executionContext;
        if (toolContext is not null)
        {
            toolContext = ApplyRuntimeState(toolContext, BuildRuntimeStateFromToolResult(tool.Name, toolResult));
            if (toolContext.RuntimeState is not null)
            {
                EnrichMetadataWithRuntimeState(toolPathMetadata, toolContext.RuntimeState);
            }
        }

        var executionMode = DetermineExecutionMode(tool.Name, toolResult);

        await LogEventAsync(request, "ToolExecutionCompleted", safety.ToString(), riskLevel.ToString(), tool.Name, executionMode, toolResult.Success ? "Success" : "Failed", toolResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, toolContext, toolPathMetadata);

        var finalResult = new CommandResult
        {
            Status = toolResult.Success ? "Accepted" : "Denied",
            Safety = safety.ToString(),
            RiskLevel = riskLevel.ToString(),
            Decision = decision.DecisionText,
            SelectedTool = tool.Name,
            ToolExecutionResult = toolResult.Output,
            Message = approvalPath
                ? $"Approved by user. {toolResult.Message}"
                : toolResult.Message
        };

        await LogEventAsync(request, "CommandCompleted", finalResult.Safety, finalResult.RiskLevel, finalResult.SelectedTool, executionMode, finalResult.Status, finalResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, toolContext, toolPathMetadata);

        return finalResult;
    }

    private async Task<CommandResult> BuildLaunchGroundingBlockedResultAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        AgentExecutionContext executionContext,
        AiDecision decision,
        bool approvalPath,
        CancellationToken cancellationToken,
        bool? snapshotUsed = null,
        bool? snapshotFallback = null,
        bool? targetReasonNormalized = null,
        string? targetReasonSource = null,
        bool? snapshotContextUsed = null,
        bool? snapshotAdapterPreserved = null,
        bool? snapshotObservationPreserved = null,
        DecisionInputBundle? decisionInputBundle = null,
        DecisionSummary? decisionSummary = null,
        AiDecisionInput? aiDecisionInput = null)
    {
        var metadata = CreateExecutionPathMetadata(
            "tool",
            null,
            snapshotUsed,
            snapshotFallback,
            targetReasonNormalized,
            targetReasonSource,
            snapshotContextUsed,
            snapshotAdapterPreserved,
            snapshotObservationPreserved);
        EnrichMetadataWithDecisionInput(metadata, decisionInputBundle);
        EnrichMetadataWithDecisionSummary(metadata, decisionSummary);
        EnrichMetadataWithAiDecisionInput(metadata, aiDecisionInput);
        EnrichMetadataWithNextActionDecision(metadata, decision);
        metadata["launch_grounding_required"] = "true";
        metadata["launch_grounding_satisfied"] = "false";

        var result = new CommandResult
        {
            Status = "Denied",
            Safety = safety.ToString(),
            RiskLevel = riskLevel.ToString(),
            Decision = decision.DecisionText,
            SelectedTool = ResolveDecisionToolNameForExecution(decision),
            ToolExecutionResult = "Not executed",
            Message = "Blocked execution: executable launch grounding is required for app/path execution.",
            RuntimeTerminalState = DecisionLoopTerminalState.Blocked
        };

        await LogEventAsync(
            request,
            "ExecutionBlocked",
            result.Safety,
            result.RiskLevel,
            result.SelectedTool,
            "not-executed",
            result.Status,
            result.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            executionContext,
            metadata);

        await LogEventAsync(
            request,
            "CommandCompleted",
            result.Safety,
            result.RiskLevel,
            result.SelectedTool,
            "not-executed",
            result.Status,
            result.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            executionContext,
            metadata);

        return result;
    }

    private async Task<CommandResult?> TryExecuteCapabilityPathAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        AgentExecutionContext executionContext,
        AiDecision decision,
        bool approvalPath,
        CancellationToken cancellationToken,
        bool? snapshotUsed = null,
        bool? snapshotFallback = null,
        bool? targetReasonNormalized = null,
        string? targetReasonSource = null,
        bool? snapshotContextUsed = null,
        bool? snapshotAdapterPreserved = null,
        bool? snapshotObservationPreserved = null,
        DecisionInputBundle? decisionInputBundle = null,
        DecisionSummary? decisionSummary = null,
        AiDecisionInput? aiDecisionInput = null)
    {
        if (_capabilityRegistry is null || _capabilityInvocationRouterRegistry is null)
        {
            return null;
        }

        if (!_capabilityInvocationRouterRegistry.TryRoute(
            decision,
            executionContext,
            out var invocation))
        {
            return null;
        }

        var capabilityName = invocation.RoutedCapabilityName;
        var target = invocation.Target;
        var action = invocation.Action;
        var routingSource = invocation.RoutingSource;
        var routedContext = EnsureInvocationTargetInContext(executionContext, target);

        var capability = _capabilityRegistry.FindByName(capabilityName);
        if (capability is null)
        {
            return null;
        }

        if (!capability.CanHandle(action, executionContext))
        {
            return null;
        }

        var capabilityMetadata = CreateExecutionPathMetadata(
            "capability",
            capability.Name,
            snapshotUsed,
            snapshotFallback,
            targetReasonNormalized,
            targetReasonSource,
            snapshotContextUsed,
            snapshotAdapterPreserved,
            snapshotObservationPreserved);
        EnrichMetadataWithDecisionInput(capabilityMetadata, decisionInputBundle);
        EnrichMetadataWithDecisionSummary(capabilityMetadata, decisionSummary);
        EnrichMetadataWithAiDecisionInput(capabilityMetadata, aiDecisionInput);
        EnrichMetadataWithNextActionDecision(capabilityMetadata, decision);
        capabilityMetadata["typed_intent_kind"] = executionContext.DetectedIntent.ToString();
        capabilityMetadata["capability_routing_source"] = routingSource;
        capabilityMetadata["tool_bridge_fallback_used"] = routingSource.Equals("legacy-tool-bridge", StringComparison.OrdinalIgnoreCase)
            ? "true"
            : "false";
        EnrichObservationRoutingPolicyMetadata(capabilityMetadata, routingSource);
        EnrichServiceIntentKindMetadata(capabilityMetadata, decision);
        EnrichFileIntentKindMetadata(capabilityMetadata, decision);
        EnrichCapabilityMetadataWithAdapterContext(capabilityMetadata, routedContext, target);
        EnrichWindowProcessCapabilityMetadata(capabilityMetadata, capability.Name, target);
        EnrichProcessVerificationCapabilityMetadata(capabilityMetadata, capability.Name, target);
        EnrichForegroundAlignmentCapabilityMetadata(capabilityMetadata, capability.Name, target);
        EnrichServiceStatusCapabilityMetadata(capabilityMetadata, capability.Name, target);
        EnrichServiceControlCapabilityMetadata(capabilityMetadata, capability.Name, action, target);
        EnrichFileVerificationCapabilityMetadata(capabilityMetadata, capability.Name, target);
        EnrichFileOpenCapabilityMetadata(capabilityMetadata, capability.Name, target);

        await LogEventAsync(
            request,
            "CapabilityResolved",
            safety.ToString(),
            riskLevel.ToString(),
            capability.Name,
            "not-executed",
            "Resolved",
            "Capability resolved successfully.",
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            routedContext,
            capabilityMetadata);

        var runtimeSliceSelectionSource = DetermineRuntimeSliceSelectionSource();
        var runtimeSliceSelection = await TrySelectAndRunRuntimeSliceAsync(routedContext, action, cancellationToken);
        if (runtimeSliceSelection is not null)
        {
            capabilityMetadata["selected_runtime_slice"] = runtimeSliceSelection.SliceName;
            capabilityMetadata["runtime_slice_selection_source"] = runtimeSliceSelectionSource;
            capabilityMetadata["step_runtime_engagement"] = runtimeSliceSelection.Result.EngagementDecision.Disposition.ToString();
            capabilityMetadata["step_runtime_engagement_message"] = runtimeSliceSelection.Result.EngagementDecision.Message;
            return await ExecuteCapabilityWithStepRuntimeAsync(
                request,
                safety,
                riskLevel,
                routedContext,
                decision,
                capability,
                runtimeSliceSelection.Result,
                approvalPath,
                cancellationToken,
                capabilityMetadata);
        }

        if (IsFailClosedFallback)
        {
            capabilityMetadata["selected_runtime_slice"] = "none";
            capabilityMetadata["runtime_slice_selection_source"] = runtimeSliceSelectionSource;
            return await BuildFailClosedFallbackResultAsync(
                request,
                executionContext,
                decision,
                capability.Name,
                approvalPath,
                runtimeSliceSelectionSource,
                capabilityMetadata,
                cancellationToken);
        }

        capabilityMetadata["selected_runtime_slice"] = "none";
        capabilityMetadata["runtime_slice_selection_source"] = runtimeSliceSelectionSource;
        capabilityMetadata["runtime_slice_fallback_kind"] = "controlled";

        var capabilityResult = await capability.ExecuteAsync(action, routedContext, cancellationToken);
        var capabilityContext = routedContext;

        (capabilityResult, capabilityContext) = await ApplyVerificationFeedbackAsync(
            request,
            safety,
            riskLevel,
            action,
            decision.NextActionDecision?.ExecuteAction?.ActionType,
            capabilityResult,
            capabilityContext,
            cancellationToken,
            capabilityMetadata);

        capabilityContext = ApplyRuntimeState(
            capabilityContext,
            BuildRuntimeState(action, capabilityResult, capabilityContext.PrimaryTargetGrounding));
        if (capabilityContext.RuntimeState is not null)
        {
            EnrichMetadataWithRuntimeState(capabilityMetadata, capabilityContext.RuntimeState);
        }

        EnrichWindowProcessCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichProcessVerificationCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichForegroundAlignmentCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichServiceStatusCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichServiceControlCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichFileVerificationCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichFileOpenCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        var executionMode = DetermineCapabilityExecutionMode(capabilityResult);
        var accepted = IsCapabilitySuccessful(capabilityResult.Status);

        await LogEventAsync(
            request,
            "CapabilityExecutionCompleted",
            safety.ToString(),
            riskLevel.ToString(),
            capability.Name,
            executionMode,
            accepted ? "Success" : "Failed",
            capabilityResult.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            capabilityContext,
            capabilityMetadata);

        var finalResult = new CommandResult
        {
            Status = accepted ? "Accepted" : "Denied",
            Safety = safety.ToString(),
            RiskLevel = riskLevel.ToString(),
            Decision = decision.DecisionText,
            SelectedTool = capability.Name,
            ToolExecutionResult = string.IsNullOrWhiteSpace(capabilityResult.OutputText)
                ? "Not executed"
                : capabilityResult.OutputText,
            Message = approvalPath
                ? $"Approved by user. {capabilityResult.Message}"
                : capabilityResult.Message,
            VerificationStatus = capabilityResult.Verification?.Status,
            VerificationReason = capabilityResult.Verification?.Reason
        };

        await LogEventAsync(
            request,
            "CommandCompleted",
            finalResult.Safety,
            finalResult.RiskLevel,
            finalResult.SelectedTool,
            executionMode,
            finalResult.Status,
            finalResult.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            capabilityContext,
            capabilityMetadata);

        return finalResult;
    }

    private async Task<RuntimeSliceSelection?> TrySelectAndRunRuntimeSliceAsync(
        AgentExecutionContext executionContext,
        AgentAction proposedAction,
        CancellationToken cancellationToken)
    {
        if (_runtimeSliceRegistry is null)
        {
            return null;
        }

        return await _runtimeSliceRegistry.TrySelectAndRunAsync(
            executionContext,
            proposedAction,
            cancellationToken);
    }

    private bool IsFailClosedFallback => _runtimeSliceFallbackPolicy == RuntimeSliceFallbackPolicy.FailClosed;

    private async Task<CommandResult> BuildFailClosedFallbackResultAsync(
        CommandRequest request,
        AgentExecutionContext executionContext,
        AiDecision decision,
        string selectedTool,
        bool approvalPath,
        string runtimeSliceSelectionSource,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        metadata["runtime_slice_fallback_kind"] = "fail_closed";
        metadata["runtime_slice_selection_source"] = runtimeSliceSelectionSource;
        metadata["selected_runtime_slice"] = "none";

        var message = "Blocked by runtime slice fallback policy: no runtime slice engaged for this request.";
        var failClosedResult = new CommandResult
        {
            Status = "Denied",
            Safety = SafetyDisposition.Denied.ToString(),
            RiskLevel = SafetyRiskLevel.High.ToString(),
            Decision = decision.DecisionText,
            SelectedTool = selectedTool,
            ToolExecutionResult = "Not executed",
            Message = message,
            RuntimeTerminalState = DecisionLoopTerminalState.Blocked
        };

        await LogEventAsync(
            request,
            "ExecutionBlocked",
            failClosedResult.Safety,
            failClosedResult.RiskLevel,
            failClosedResult.SelectedTool,
            "not-executed",
            failClosedResult.Status,
            failClosedResult.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            executionContext,
            metadata);

        await LogEventAsync(
            request,
            "CommandCompleted",
            failClosedResult.Safety,
            failClosedResult.RiskLevel,
            failClosedResult.SelectedTool,
            "not-executed",
            failClosedResult.Status,
            failClosedResult.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            executionContext,
            metadata);

        return failClosedResult;
    }

    private string DetermineRuntimeSliceSelectionSource()
    {
        if (_runtimeSliceRegistry is not null)
        {
            return "registry";
        }

        return "disabled";
    }

    private async Task<CommandResult> ExecuteCapabilityWithStepRuntimeAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        AgentExecutionContext executionContext,
        AiDecision decision,
        ICapability capability,
        IRuntimeSliceResult runtimeSliceResult,
        bool approvalPath,
        CancellationToken cancellationToken,
        IDictionary<string, string> capabilityMetadata)
    {
        var stepOutcome = runtimeSliceResult.Outcome ?? new StepOutcome
        {
            State = runtimeSliceResult.InitialState ?? new AgentStepState
            {
                ExecutionContext = executionContext,
                CurrentObservation = executionContext.Observation,
                StepIndex = 0
            },
            Decision = runtimeSliceResult.InitialDecision ?? new StepDecision
            {
                Disposition = StepContinuationDisposition.Stop,
                Message = "Runtime slice did not produce an executable step outcome."
            },
            Disposition = StepContinuationDisposition.Stop,
            Message = runtimeSliceResult.InitialDecision?.Message ?? "Runtime slice did not produce an executable step outcome."
        };
        var stepFeedbackHistory = stepOutcome.FeedbackHistory.Count > 0
            ? stepOutcome.FeedbackHistory
            : stepOutcome.Feedback is null
                ? []
                : [stepOutcome.Feedback];
        var primaryFeedback = stepFeedbackHistory
                                  .LastOrDefault(feedback =>
                                      feedback.ExecutedAction.CapabilityName.Equals(
                                          capability.Name,
                                          StringComparison.OrdinalIgnoreCase))
                              ?? stepFeedbackHistory.FirstOrDefault();
        var followUpFeedback = stepFeedbackHistory.Count > 1 ? stepFeedbackHistory[^1] : null;

        capabilityMetadata["step_runtime_used"] = "true";
        capabilityMetadata["step_runtime_disposition"] = stepOutcome.Disposition.ToString();
        capabilityMetadata["step_runtime_step_index"] = stepOutcome.State.StepIndex.ToString();
        capabilityMetadata["step_runtime_feedback_available"] = stepOutcome.Feedback is null ? "false" : "true";
        capabilityMetadata["step_runtime_step_count"] = stepFeedbackHistory.Count.ToString();
        capabilityMetadata["step_runtime_follow_up_executed"] = followUpFeedback is null ? "false" : "true";

        if (stepOutcome.Feedback is not null)
        {
            capabilityMetadata["step_runtime_observation_refresh_status"] = stepOutcome.Feedback.ObservationRefreshStatus;
        }

        if (followUpFeedback is not null)
        {
            capabilityMetadata["step_runtime_follow_up_capability"] = followUpFeedback.ExecutedAction.CapabilityName;
            capabilityMetadata["step_runtime_follow_up_action"] = followUpFeedback.ExecutedAction.ActionName;
            capabilityMetadata["step_runtime_follow_up_status"] = followUpFeedback.ExecutionResult.Status.ToString();
            capabilityMetadata["step_runtime_follow_up_observation_refresh_status"] = followUpFeedback.ObservationRefreshStatus;
        }

        var resultContext = stepOutcome.State.ExecutionContext;
        var capabilityResult = primaryFeedback?.ExecutionResult;
        if (capabilityResult is null)
        {
            var stepSafetyDecision = stepOutcome.SafetyDecision;
            if (stepSafetyDecision is not null && stepOutcome.Decision.NextAction is not null)
            {
                await LogEventAsync(
                    request,
                    "PolicyEvaluated",
                    stepSafetyDecision.Disposition.ToString(),
                    stepSafetyDecision.RiskLevel.ToString(),
                    capability.Name,
                    "not-executed",
                    "Evaluated",
                    stepSafetyDecision.Reason,
                    approvalPath ? "Approved" : string.Empty,
                    cancellationToken,
                    resultContext,
                    BuildPolicyEvaluationMetadata(
                        "step",
                        new StepSafetyRequest
                        {
                            Source = StepSafetyRequestSource.StepRuntime,
                            CommandText = request.UserInput,
                            StepIndex = stepOutcome.State.StepIndex,
                            AgentAction = stepOutcome.Decision.NextAction
                        },
                        stepSafetyDecision));
            }

            var executionMode = stepOutcome.Disposition == StepContinuationDisposition.RequiresApproval
                ? "approval-needed"
                : "not-executed";
            var nonExecutingStatus = stepOutcome.Disposition == StepContinuationDisposition.RequiresApproval
                ? "PendingApproval"
                : "Denied";
            var nonExecutingSafety = stepOutcome.Disposition == StepContinuationDisposition.RequiresApproval
                ? SafetyDisposition.RequiresApproval
                : SafetyDisposition.Denied;
            var nonExecutingRiskLevel = stepSafetyDecision?.RiskLevel ?? riskLevel;
            PendingApprovalSnapshot? pendingApprovalSnapshot = null;
            var nonExecutingDecisionText = decision.DecisionText;

            if (stepOutcome.Disposition == StepContinuationDisposition.RequiresApproval)
            {
                if (!TryBuildRuntimeApprovalDecision(stepOutcome.Decision.NextAction, stepOutcome.Message, out var runtimeApprovalDecision))
                {
                    nonExecutingStatus = "Denied";
                    executionMode = "not-executed";
                    nonExecutingSafety = SafetyDisposition.Denied;
                    nonExecutingRiskLevel = SafetyRiskLevel.High;
                    nonExecutingDecisionText = decision.DecisionText;
                }
                else
                {
                    nonExecutingDecisionText = runtimeApprovalDecision.DecisionText;
                    pendingApprovalSnapshot = BuildPendingApprovalSnapshot(
                        request,
                        runtimeApprovalDecision,
                        resultContext.ResolvedTargets,
                        resultContext.ContextAdapter,
                        BuildPendingObservationSummary(resultContext.Observation));
                }
            }

            var nonExecutingResult = new CommandResult
            {
                Status = nonExecutingStatus,
                Safety = nonExecutingSafety.ToString(),
                RiskLevel = nonExecutingRiskLevel.ToString(),
                Decision = nonExecutingDecisionText,
                SelectedTool = capability.Name,
                ToolExecutionResult = "Not executed",
                Message = stepOutcome.Message,
                PendingApprovalSnapshot = pendingApprovalSnapshot
            };

            await LogEventAsync(
                request,
                "CapabilityExecutionCompleted",
                nonExecutingResult.Safety,
                nonExecutingResult.RiskLevel,
                capability.Name,
                executionMode,
                nonExecutingStatus,
                nonExecutingResult.Message,
                approvalPath ? "Approved" : string.Empty,
                cancellationToken,
                resultContext,
                capabilityMetadata);

            await LogEventAsync(
                request,
                "CommandCompleted",
                nonExecutingResult.Safety,
                nonExecutingResult.RiskLevel,
                nonExecutingResult.SelectedTool,
                executionMode,
                nonExecutingResult.Status,
                nonExecutingResult.Message,
                approvalPath ? "Approved" : string.Empty,
                cancellationToken,
                resultContext,
                capabilityMetadata);

            return nonExecutingResult;
        }

        var executedAction = primaryFeedback?.ExecutedAction ?? new AgentAction
        {
            CapabilityName = capability.Name,
            ActionName = "Unknown"
        };

        (capabilityResult, resultContext) = await ApplyVerificationFeedbackAsync(
            request,
            safety,
            riskLevel,
            executedAction,
            decision.NextActionDecision?.ExecuteAction?.ActionType,
            capabilityResult,
            resultContext,
            cancellationToken,
            capabilityMetadata);

        resultContext = ApplyRuntimeState(
            resultContext,
            BuildRuntimeState(executedAction, capabilityResult, resultContext.PrimaryTargetGrounding));
        if (resultContext.RuntimeState is not null)
        {
            EnrichMetadataWithRuntimeState(capabilityMetadata, resultContext.RuntimeState);
        }

        EnrichWindowProcessCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichProcessVerificationCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichForegroundAlignmentCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichServiceStatusCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichServiceControlCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichFileVerificationCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);
        EnrichFileOpenCapabilityResultMetadata(capabilityMetadata, capability.Name, capabilityResult);

        if (followUpFeedback is not null)
        {
            EnrichWindowProcessCapabilityResultMetadata(
                capabilityMetadata,
                followUpFeedback.ExecutedAction.CapabilityName,
                followUpFeedback.ExecutionResult);
            EnrichForegroundAlignmentCapabilityResultMetadata(
                capabilityMetadata,
                followUpFeedback.ExecutedAction.CapabilityName,
                followUpFeedback.ExecutionResult);
        }

        var executionModeResult = DetermineCapabilityExecutionMode(capabilityResult);
        var accepted = IsCapabilitySuccessful(capabilityResult.Status);
        var toolExecutionOutputs = stepFeedbackHistory
            .Select(feedback => feedback.ExecutionResult.OutputText)
            .Where(outputText => !string.IsNullOrWhiteSpace(outputText))
            .Cast<string>()
            .ToArray();

        await LogEventAsync(
            request,
            "CapabilityExecutionCompleted",
            safety.ToString(),
            riskLevel.ToString(),
            capability.Name,
            executionModeResult,
            accepted ? "Success" : "Failed",
            capabilityResult.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            resultContext,
            capabilityMetadata);

        var finalResult = new CommandResult
        {
            Status = accepted ? "Accepted" : "Denied",
            Safety = safety.ToString(),
            RiskLevel = riskLevel.ToString(),
            Decision = decision.DecisionText,
            SelectedTool = capability.Name,
            ToolExecutionResult = toolExecutionOutputs.Length == 0
                ? "Not executed"
                : string.Join(" | ", toolExecutionOutputs),
            Message = approvalPath
                ? $"Approved by user. {stepOutcome.Message}"
                : stepOutcome.Message,
            VerificationStatus = capabilityResult.Verification?.Status,
            VerificationReason = capabilityResult.Verification?.Reason
        };

        await LogEventAsync(
            request,
            "CommandCompleted",
            finalResult.Safety,
            finalResult.RiskLevel,
            finalResult.SelectedTool,
            executionModeResult,
            finalResult.Status,
            finalResult.Message,
            approvalPath ? "Approved" : string.Empty,
            cancellationToken,
            resultContext,
            capabilityMetadata);

        return finalResult;
    }

    private static AiDecision NormalizeDecision(AiDecision decision)
    {
        var nextActionDecision = decision.NextActionDecision;
        var synthesizedFromLegacy = false;
        if (nextActionDecision is null)
        {
            synthesizedFromLegacy = TryBuildExecuteNextActionFromLegacyDecision(decision, out nextActionDecision);
        }

        if (nextActionDecision is null)
        {
            return decision;
        }

        var selectedToolName = decision.SelectedToolName;
        var toolArgument = decision.ToolArgument;

        if (nextActionDecision.Kind == DecisionKind.ExecuteAction &&
            nextActionDecision.ExecuteAction is not null)
        {
            if (string.IsNullOrWhiteSpace(selectedToolName))
            {
                selectedToolName = MapActionTypeToToolName(nextActionDecision.ExecuteAction.ActionType);
            }

            if (string.IsNullOrWhiteSpace(toolArgument))
            {
                toolArgument = ExtractToolArgumentFromExecuteAction(nextActionDecision.ExecuteAction);
            }
        }

        var decisionText = string.IsNullOrWhiteSpace(decision.DecisionText)
            ? BuildDecisionTextFromNextAction(nextActionDecision)
            : decision.DecisionText;

        var routedCapabilityName = string.IsNullOrWhiteSpace(decision.RoutedCapabilityName)
            ? nextActionDecision.ExecuteAction?.CapabilityHint
            : decision.RoutedCapabilityName;

        var routedActionName = string.IsNullOrWhiteSpace(decision.RoutedActionName) &&
                               nextActionDecision.ExecuteAction is not null
            ? nextActionDecision.ExecuteAction.ActionType.ToString()
            : decision.RoutedActionName;

        return new AiDecision
        {
            SelectedToolName = selectedToolName,
            ToolArgument = toolArgument,
            DecisionText = decisionText,
            NextActionDecision = nextActionDecision,
            RoutedCapabilityName = routedCapabilityName,
            RoutedActionName = routedActionName,
            IsLegacyFallback = decision.IsLegacyFallback || synthesizedFromLegacy
        };
    }

    private static string ResolveDecisionToolNameForExecution(AiDecision decision)
    {
        if (decision.IsLegacyFallback)
        {
            return decision.SelectedToolName;
        }

        var nextActionDecision = decision.NextActionDecision;
        if (nextActionDecision is not null &&
            nextActionDecision.Kind == DecisionKind.ExecuteAction &&
            nextActionDecision.ExecuteAction is not null)
        {
            var mappedToolName = MapActionTypeToToolName(nextActionDecision.ExecuteAction.ActionType);
            if (!string.IsNullOrWhiteSpace(mappedToolName))
            {
                return mappedToolName;
            }
        }

        return decision.SelectedToolName;
    }

    private static string ResolveDecisionToolNameForDisplay(AiDecision decision)
    {
        var executionToolName = ResolveDecisionToolNameForExecution(decision);
        if (!string.IsNullOrWhiteSpace(executionToolName))
        {
            return executionToolName;
        }

        var nextActionDecision = decision.NextActionDecision;
        if (nextActionDecision is not null &&
            nextActionDecision.Kind == DecisionKind.AskApproval &&
            nextActionDecision.AskApproval?.ProposedAction is not null)
        {
            return MapActionTypeToToolName(nextActionDecision.AskApproval.ProposedAction.ActionType);
        }

        return decision.SelectedToolName;
    }

    private static string? ResolveDecisionToolArgumentForExecution(AiDecision decision)
    {
        if (decision.IsLegacyFallback)
        {
            return decision.ToolArgument;
        }

        var nextActionDecision = decision.NextActionDecision;
        if (nextActionDecision is not null &&
            nextActionDecision.Kind == DecisionKind.ExecuteAction &&
            nextActionDecision.ExecuteAction is not null)
        {
            var executeActionArgument = ExtractToolArgumentFromExecuteAction(nextActionDecision.ExecuteAction);
            if (!string.IsNullOrWhiteSpace(executeActionArgument))
            {
                return executeActionArgument;
            }
        }

        return decision.ToolArgument;
    }

    private static bool RequiresExecutableLaunchGrounding(
        AiDecision decision,
        AgentExecutionContext executionContext)
    {
        if (decision.NextActionDecision?.ExecuteAction?.ActionType == ActionType.Launch &&
            !decision.IsLegacyFallback)
        {
            return true;
        }

        if (string.Equals(
                decision.RoutedCapabilityName,
                "ApplicationCapability",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            decision.SelectedToolName,
            "OpenAppTool",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExecutableLaunchGrounding(TargetGroundingResult? grounding)
    {
        return grounding is not null &&
               grounding.Disposition == TargetGroundingDisposition.Resolved &&
               grounding.ExecutionSuitability == TargetExecutionSuitability.ExecutableHere &&
               grounding.Target.Kind is GroundedTargetKind.KnownApplication or GroundedTargetKind.PathLike &&
               !string.IsNullOrWhiteSpace(grounding.Target.CanonicalValue);
    }

    private static AiDecision RewriteAskApprovalDecisionAsExecuteAction(AiDecision decision)
    {
        var nextActionDecision = decision.NextActionDecision
            ?? throw new InvalidOperationException("AskApproval rewrite requires a next-action decision.");
        var proposedAction = nextActionDecision.AskApproval?.ProposedAction
            ?? throw new InvalidOperationException("AskApproval rewrite requires a proposed action.");

        var decisionText = string.IsNullOrWhiteSpace(decision.DecisionText)
            ? BuildDecisionTextFromNextAction(nextActionDecision)
            : decision.DecisionText;
        var actionMessage = string.IsNullOrWhiteSpace(nextActionDecision.Message)
            ? decisionText
            : nextActionDecision.Message;

        return NormalizeDecision(new AiDecision
        {
            SelectedToolName = MapActionTypeToToolName(proposedAction.ActionType),
            ToolArgument = ExtractToolArgumentFromExecuteAction(proposedAction),
            DecisionText = decisionText,
            NextActionDecision = new NextActionDecision
            {
                Kind = DecisionKind.ExecuteAction,
                Message = actionMessage,
                Rationale = nextActionDecision.Rationale,
                Confidence = nextActionDecision.Confidence,
                ExecuteAction = proposedAction,
                Metadata = nextActionDecision.Metadata
            },
            RoutedCapabilityName = decision.RoutedCapabilityName,
            RoutedActionName = decision.RoutedActionName,
            IsLegacyFallback = decision.IsLegacyFallback
        });
    }

    private static bool TryBuildExecuteNextActionFromLegacyDecision(
        AiDecision decision,
        out NextActionDecision? nextActionDecision)
    {
        nextActionDecision = null;

        if (string.IsNullOrWhiteSpace(decision.SelectedToolName))
        {
            return false;
        }

        if (!TryMapToolNameToActionType(decision.SelectedToolName, out var actionType))
        {
            actionType = ActionType.Unknown;
        }

        var target = BuildFallbackActionTarget(actionType, decision.SelectedToolName, decision.ToolArgument);
        var parameters = new ActionParameters
        {
            Values = BuildFallbackActionParameterDictionary(actionType, decision.SelectedToolName, decision.ToolArgument)
        };
        nextActionDecision = new NextActionDecision
        {
            Kind = DecisionKind.ExecuteAction,
            Message = string.IsNullOrWhiteSpace(decision.DecisionText)
                ? $"Legacy decision mapped to {actionType}."
                : decision.DecisionText,
            ExecuteAction = new ExecuteActionPayload
            {
                ActionType = actionType,
                Target = target,
                Parameters = parameters,
                CapabilityHint = decision.RoutedCapabilityName,
                RetryHint = null
            }
        };

        return true;
    }

    private static string MapActionTypeToToolName(ActionType actionType)
    {
        return actionType switch
        {
            ActionType.Launch => "OpenAppTool",
            ActionType.Focus => "FocusWindowTool",
            ActionType.InputText => "TypeTextTool",
            ActionType.Confirm => "PressKeyTool",
            ActionType.Cancel => "PressKeyTool",
            ActionType.PressKey => "PressKeyTool",
            ActionType.PressShortcut => "PressShortcutTool",
            ActionType.Verify => "VerifyProcessTool",
            ActionType.OpenFile => "OpenExistingFileTool",
            _ => string.Empty
        };
    }

    private static bool TryMapToolNameToActionType(string toolName, out ActionType actionType)
    {
        if (toolName.Equals("OpenAppTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Launch;
            return true;
        }

        if (toolName.Equals("FocusWindowTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Focus;
            return true;
        }

        if (toolName.Equals("TypeTextTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.InputText;
            return true;
        }

        if (toolName.Equals("PressKeyTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.PressKey;
            return true;
        }

        if (toolName.Equals("PressShortcutTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.PressShortcut;
            return true;
        }

        if (toolName.Equals("VerifyProcessTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Verify;
            return true;
        }

        if (toolName.Equals("VerifyFileExistsTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Verify;
            return true;
        }

        if (toolName.Equals("OpenExistingFileTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.OpenFile;
            return true;
        }

        if (toolName.Equals("VerifyServiceStatusTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Verify;
            return true;
        }

        if (toolName.Equals("StartServiceTool", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("StopServiceTool", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Launch;
            return true;
        }

        actionType = ActionType.Unknown;
        return false;
    }

    private static IReadOnlyDictionary<string, string> BuildFallbackActionParameterDictionary(
        ActionType actionType,
        string toolName,
        string? toolArgument)
    {
        if (string.IsNullOrWhiteSpace(toolArgument))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var key = toolName switch
        {
            "VerifyFileExistsTool" => "path",
            "OpenExistingFileTool" => "path",
            "VerifyServiceStatusTool" => "service",
            "StartServiceTool" => "service",
            "StopServiceTool" => "service",
            _ => actionType switch
            {
                ActionType.InputText => "text",
                ActionType.PressKey => "key",
                ActionType.Confirm => "key",
                ActionType.Cancel => "key",
                ActionType.PressShortcut => "shortcut",
                ActionType.OpenFile => "path",
                ActionType.Launch => "app",
                ActionType.Verify => "target",
                _ => "value"
            }
        };

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = toolArgument
        };
    }

    private static ActionTarget BuildFallbackActionTarget(ActionType actionType, string toolName, string? toolArgument)
    {
        return new ActionTarget
        {
            Kind = MapActionTypeToFallbackTargetKind(actionType, toolName),
            Reference = string.IsNullOrWhiteSpace(toolArgument)
                ? "active-context"
                : toolArgument
        };
    }

    private static ActionTargetKind MapActionTypeToFallbackTargetKind(ActionType actionType, string toolName)
    {
        if (toolName.Equals("FocusWindowTool", StringComparison.OrdinalIgnoreCase))
        {
            return ActionTargetKind.Window;
        }

        if (toolName.Equals("VerifyProcessTool", StringComparison.OrdinalIgnoreCase))
        {
            return ActionTargetKind.Process;
        }

        if (toolName.Equals("VerifyFileExistsTool", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("OpenExistingFileTool", StringComparison.OrdinalIgnoreCase))
        {
            return ActionTargetKind.File;
        }

        if (toolName.Equals("VerifyServiceStatusTool", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("StartServiceTool", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("StopServiceTool", StringComparison.OrdinalIgnoreCase))
        {
            return ActionTargetKind.Service;
        }

        return actionType switch
        {
            ActionType.Launch => ActionTargetKind.Application,
            ActionType.Verify => ActionTargetKind.Process,
            ActionType.OpenFile => ActionTargetKind.File,
            _ => ActionTargetKind.Generic
        };
    }


    private static string? ExtractToolArgumentFromExecuteAction(ExecuteActionPayload executeAction)
    {
        var actionType = executeAction.ActionType;

        if (actionType == ActionType.InputText &&
            TryGetActionParameterValue(executeAction.Parameters, ["text", "input", "value"], out var textArgument))
        {
            return textArgument;
        }

        if ((actionType == ActionType.PressKey || actionType == ActionType.Confirm || actionType == ActionType.Cancel) &&
            TryGetActionParameterValue(executeAction.Parameters, ["key", "value"], out var keyArgument))
        {
            return keyArgument;
        }

        if (actionType == ActionType.Confirm)
        {
            return "enter";
        }

        if (actionType == ActionType.Cancel)
        {
            return "escape";
        }

        if (actionType == ActionType.PressShortcut &&
            TryGetActionParameterValue(executeAction.Parameters, ["shortcut", "value"], out var shortcutArgument))
        {
            return shortcutArgument;
        }

        if (actionType == ActionType.OpenFile &&
            TryGetActionParameterValue(executeAction.Parameters, ["file", "path", "target"], out var fileArgument))
        {
            return fileArgument;
        }

        if (actionType == ActionType.Launch &&
            TryGetActionParameterValue(executeAction.Parameters, ["app", "path", "target"], out var appArgument))
        {
            return appArgument;
        }

        return string.IsNullOrWhiteSpace(executeAction.Target.Reference)
            ? null
            : executeAction.Target.Reference;
    }

    private static bool TryGetActionParameterValue(
        ActionParameters parameters,
        IEnumerable<string> candidateKeys,
        out string value)
    {
        value = string.Empty;

        foreach (var candidateKey in candidateKeys)
        {
            foreach (var pair in parameters.Values)
            {
                if (!pair.Key.Equals(candidateKey, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                value = pair.Value;
                return true;
            }
        }

        return false;
    }

    private static string BuildDecisionTextFromNextAction(NextActionDecision nextActionDecision)
    {
        if (!string.IsNullOrWhiteSpace(nextActionDecision.Message))
        {
            return nextActionDecision.Message;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.Rationale))
        {
            return nextActionDecision.Rationale;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.StopReason))
        {
            return nextActionDecision.StopReason;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.RetryReason))
        {
            return nextActionDecision.RetryReason;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.RequiresApprovalReason))
        {
            return nextActionDecision.RequiresApprovalReason;
        }

        return $"Next-action decision: {nextActionDecision.Kind}.";
    }

    private async Task<MetaDecisionHandlingResult> TryHandleMetaDecisionAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        bool approvalPath,
        AgentExecutionContext? executionContext,
        AiDecision decision,
        CancellationToken cancellationToken,
        DecisionInputBundle? decisionInputBundle = null,
        DecisionSummary? decisionSummary = null,
        AiDecisionInput? aiDecisionInput = null,
        IDictionary<string, string>? metadata = null,
        bool? snapshotUsed = null,
        bool? snapshotFallback = null,
        bool? targetReasonNormalized = null,
        string? targetReasonSource = null,
        bool? snapshotContextUsed = null,
        bool? snapshotAdapterPreserved = null,
        bool? snapshotObservationPreserved = null)
    {
        var nextActionDecision = decision.NextActionDecision;
        if (nextActionDecision is null || nextActionDecision.Kind == DecisionKind.ExecuteAction)
        {
            return new MetaDecisionHandlingResult
            {
                Decision = decision
            };
        }

        var decisionText = string.IsNullOrWhiteSpace(decision.DecisionText)
            ? BuildDecisionTextFromNextAction(nextActionDecision)
            : decision.DecisionText;

        var effectiveMetadata = metadata ?? CreateExecutionPathMetadata(
            "meta-decision",
            null,
            snapshotUsed,
            snapshotFallback,
            targetReasonNormalized,
            targetReasonSource,
            snapshotContextUsed,
            snapshotAdapterPreserved,
            snapshotObservationPreserved);

        EnrichMetadataWithDecisionInput(effectiveMetadata, decisionInputBundle);
        EnrichMetadataWithDecisionSummary(effectiveMetadata, decisionSummary);
        EnrichMetadataWithAiDecisionInput(effectiveMetadata, aiDecisionInput);
        EnrichMetadataWithNextActionDecision(effectiveMetadata, decision);

        if (nextActionDecision.Kind == DecisionKind.AskObserve)
        {
            var observeMessage = nextActionDecision.AskObserve is null
                ? "Model requested additional observation before executing an action."
                : string.IsNullOrWhiteSpace(nextActionDecision.AskObserve.ObservationHint)
                    ? $"Model requested observation: {nextActionDecision.AskObserve.ObservationRequest}"
                    : $"Model requested observation: {nextActionDecision.AskObserve.ObservationRequest}. Hint: {nextActionDecision.AskObserve.ObservationHint}";

            var askObserveResult = new CommandResult
            {
                Status = "Denied",
                Safety = safety.ToString(),
                RiskLevel = riskLevel.ToString(),
                Decision = decisionText,
                SelectedTool = "None",
                ToolExecutionResult = "Not executed",
                Message = observeMessage
            };

            await LogEventAsync(request, "ExecutionBlocked", askObserveResult.Safety, askObserveResult.RiskLevel, askObserveResult.SelectedTool, "not-executed", askObserveResult.Status, askObserveResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
            await LogEventAsync(request, "CommandCompleted", askObserveResult.Safety, askObserveResult.RiskLevel, askObserveResult.SelectedTool, "not-executed", askObserveResult.Status, askObserveResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
            return new MetaDecisionHandlingResult
            {
                Decision = decision,
                Result = askObserveResult
            };
        }

        if (nextActionDecision.Kind == DecisionKind.AskApproval)
        {
            if (nextActionDecision.AskApproval?.ProposedAction is null)
            {
                effectiveMetadata["approval_authority_outcome"] = "policy-denied";
                const string invalidApprovalMessage = "Model requested approval without a proposed action.";

                await LogEventAsync(
                    request,
                    "PolicyEvaluated",
                    SafetyDisposition.Denied.ToString(),
                    SafetyRiskLevel.High.ToString(),
                    "None",
                    "not-executed",
                    "Evaluated",
                    invalidApprovalMessage,
                    approvalPath ? "Approved" : string.Empty,
                    cancellationToken,
                    executionContext,
                    BuildPolicyEvaluationMetadata(
                        "step",
                        approvalAuthorityOutcome: "policy-denied"));

                var invalidApprovalResult = new CommandResult
                {
                    Status = "Denied",
                    Safety = SafetyDisposition.Denied.ToString(),
                    RiskLevel = SafetyRiskLevel.High.ToString(),
                    Decision = decisionText,
                    SelectedTool = "None",
                    ToolExecutionResult = "Not executed",
                    Message = invalidApprovalMessage
                };

                await LogEventAsync(request, "ExecutionBlocked", invalidApprovalResult.Safety, invalidApprovalResult.RiskLevel, invalidApprovalResult.SelectedTool, "not-executed", invalidApprovalResult.Status, invalidApprovalResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
                await LogEventAsync(request, "CommandCompleted", invalidApprovalResult.Safety, invalidApprovalResult.RiskLevel, invalidApprovalResult.SelectedTool, "not-executed", invalidApprovalResult.Status, invalidApprovalResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
                return new MetaDecisionHandlingResult
                {
                    Decision = decision,
                    Result = invalidApprovalResult
                };
            }

            var rewrittenDecision = RewriteAskApprovalDecisionAsExecuteAction(decision);
            var stepSafetyRequest = new StepSafetyRequest
            {
                Source = StepSafetyRequestSource.DecisionLoop,
                CommandText = request.UserInput,
                StepIndex = 0,
                ExecuteAction = rewrittenDecision.NextActionDecision!.ExecuteAction
            };

            var stepSafetyDecision = await EvaluateStepSafetyAsync(stepSafetyRequest, cancellationToken);
            var approvalAuthorityOutcome = DetermineApprovalAuthorityOutcome(modelRequestedApproval: true, stepSafetyDecision);
            if (!string.IsNullOrWhiteSpace(approvalAuthorityOutcome))
            {
                effectiveMetadata["approval_authority_outcome"] = approvalAuthorityOutcome;
            }

            var selectedToolName = ResolveDecisionToolNameForDisplay(rewrittenDecision);
            if (string.IsNullOrWhiteSpace(selectedToolName))
            {
                selectedToolName = "None";
            }

            await LogEventAsync(
                request,
                "PolicyEvaluated",
                stepSafetyDecision.Disposition.ToString(),
                stepSafetyDecision.RiskLevel.ToString(),
                selectedToolName,
                "not-executed",
                "Evaluated",
                stepSafetyDecision.Reason,
                approvalPath ? "Approved" : string.Empty,
                cancellationToken,
                executionContext,
                BuildPolicyEvaluationMetadata(
                    "step",
                    stepSafetyRequest,
                    stepSafetyDecision,
                    approvalAuthorityOutcome: approvalAuthorityOutcome));

            if (stepSafetyDecision.Disposition == SafetyDisposition.Allowed)
            {
                return new MetaDecisionHandlingResult
                {
                    Decision = rewrittenDecision
                };
            }

            if (stepSafetyDecision.Disposition == SafetyDisposition.RequiresApproval)
            {
                var pendingSnapshot = BuildPendingApprovalSnapshot(
                    request,
                    decision,
                    executionContext?.ResolvedTargets ?? [],
                    executionContext?.ContextAdapter,
                    BuildPendingObservationSummary(executionContext?.Observation));

                var pendingResult = new CommandResult
                {
                    Status = "PendingApproval",
                    Safety = SafetyDisposition.RequiresApproval.ToString(),
                    RiskLevel = stepSafetyDecision.RiskLevel.ToString(),
                    Decision = decisionText,
                    SelectedTool = selectedToolName,
                    ToolExecutionResult = "Not executed",
                    Message = stepSafetyDecision.Reason,
                    PendingApprovalSnapshot = pendingSnapshot
                };

                await LogEventAsync(request, "ApprovalRequired", pendingResult.Safety, pendingResult.RiskLevel, pendingResult.SelectedTool, "not-executed", pendingResult.Status, pendingResult.Message, string.Empty, cancellationToken, executionContext, effectiveMetadata);
                await LogEventAsync(request, "CommandCompleted", pendingResult.Safety, pendingResult.RiskLevel, pendingResult.SelectedTool, "not-executed", pendingResult.Status, pendingResult.Message, string.Empty, cancellationToken, executionContext, effectiveMetadata);
                return new MetaDecisionHandlingResult
                {
                    Decision = decision,
                    Result = pendingResult
                };
            }

            var deniedApprovalResult = new CommandResult
            {
                Status = "Denied",
                Safety = SafetyDisposition.Denied.ToString(),
                RiskLevel = stepSafetyDecision.RiskLevel.ToString(),
                Decision = decisionText,
                SelectedTool = selectedToolName,
                ToolExecutionResult = "Not executed",
                Message = stepSafetyDecision.Reason
            };

            await LogEventAsync(request, "ExecutionBlocked", deniedApprovalResult.Safety, deniedApprovalResult.RiskLevel, deniedApprovalResult.SelectedTool, "not-executed", deniedApprovalResult.Status, deniedApprovalResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
            await LogEventAsync(request, "CommandCompleted", deniedApprovalResult.Safety, deniedApprovalResult.RiskLevel, deniedApprovalResult.SelectedTool, "not-executed", deniedApprovalResult.Status, deniedApprovalResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
            return new MetaDecisionHandlingResult
            {
                Decision = decision,
                Result = deniedApprovalResult
            };
        }

        if (nextActionDecision.Kind == DecisionKind.Retry)
        {
            var retryReason = nextActionDecision.RetryReason ?? nextActionDecision.Retry?.RetryReason ?? "Model requested retry for the previous action.";
            if (nextActionDecision.Retry?.RetryCountHint is int retryCountHint)
            {
                retryReason = $"{retryReason} Retry count hint: {retryCountHint}.";
            }

            var retryResult = new CommandResult
            {
                Status = "Denied",
                Safety = safety.ToString(),
                RiskLevel = riskLevel.ToString(),
                Decision = decisionText,
                SelectedTool = "None",
                ToolExecutionResult = "Not executed",
                Message = retryReason
            };

            await LogEventAsync(request, "ExecutionBlocked", retryResult.Safety, retryResult.RiskLevel, retryResult.SelectedTool, "not-executed", retryResult.Status, retryResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
            await LogEventAsync(request, "CommandCompleted", retryResult.Safety, retryResult.RiskLevel, retryResult.SelectedTool, "not-executed", retryResult.Status, retryResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
            return new MetaDecisionHandlingResult
            {
                Decision = decision,
                Result = retryResult
            };
        }

        if (nextActionDecision.Kind == DecisionKind.Stop)
        {
            var stopDisposition = nextActionDecision.Stop?.Disposition ?? StopDisposition.Blocked;
            var stopReason = nextActionDecision.StopReason ?? nextActionDecision.Stop?.StopReason ?? "Model requested stop.";
            var stopStatus = stopDisposition == StopDisposition.Completed ? "Accepted" : "Denied";

            var stopResult = new CommandResult
            {
                Status = stopStatus,
                Safety = safety.ToString(),
                RiskLevel = riskLevel.ToString(),
                Decision = decisionText,
                SelectedTool = "None",
                ToolExecutionResult = "Not executed",
                Message = stopReason
            };

            await LogEventAsync(request, "ExecutionStopped", stopResult.Safety, stopResult.RiskLevel, stopResult.SelectedTool, "not-executed", stopResult.Status, stopResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
            await LogEventAsync(request, "CommandCompleted", stopResult.Safety, stopResult.RiskLevel, stopResult.SelectedTool, "not-executed", stopResult.Status, stopResult.Message, approvalPath ? "Approved" : string.Empty, cancellationToken, executionContext, effectiveMetadata);
            return new MetaDecisionHandlingResult
            {
                Decision = decision,
                Result = stopResult
            };
        }

        return new MetaDecisionHandlingResult
        {
            Decision = decision
        };
    }

    private static AgentExecutionContext ApplyDetectedIntent(AgentExecutionContext context, AiDecision decision)
    {
        var detectedIntent = DetectIntentKind(
            context.NormalizedInput,
            context.ResolvedTargets,
            context.PrimaryTargetGrounding,
            decision);

        if (detectedIntent == context.DetectedIntent)
        {
            return context;
        }

        return new AgentExecutionContext
        {
            CorrelationId = context.CorrelationId,
            RawInput = context.RawInput,
            NormalizedInput = context.NormalizedInput,
            DetectedIntent = detectedIntent,
            CreatedAtUtc = context.CreatedAtUtc,
            SessionId = context.SessionId,
            Observation = context.Observation,
            RichObservation = context.RichObservation,
            ContextAdapter = context.ContextAdapter,
            PrimaryTargetGrounding = context.PrimaryTargetGrounding,
            RuntimeState = context.RuntimeState,
            ResolvedTargets = context.ResolvedTargets,
            Metadata = context.Metadata
        };
    }

    private static CommandIntentKind DetectIntentKind(
        string normalizedInput,
        IReadOnlyList<TargetReference> resolvedTargets,
        TargetGroundingResult? grounding,
        AiDecision? decision)
    {
        if (LooksLikePrimitiveInputIntent(normalizedInput))
        {
            return CommandIntentKind.PrimitiveInput;
        }

        if (resolvedTargets.Any(target => target.Kind == TargetKind.Service))
        {
            if (normalizedInput.StartsWith("start ", StringComparison.OrdinalIgnoreCase))
            {
                return CommandIntentKind.StartService;
            }

            if (normalizedInput.StartsWith("stop ", StringComparison.OrdinalIgnoreCase))
            {
                return CommandIntentKind.StopService;
            }

            var serviceIntentFromDecision = MapIntentFromDecision(decision);
            if (serviceIntentFromDecision is CommandIntentKind.StartService or CommandIntentKind.StopService)
            {
                return serviceIntentFromDecision;
            }

            return CommandIntentKind.VerifyServiceStatus;
        }

        if (resolvedTargets.Any(target => target.Kind == TargetKind.File))
        {
            if (normalizedInput.StartsWith("open existing file ", StringComparison.OrdinalIgnoreCase) ||
                normalizedInput.StartsWith("open file ", StringComparison.OrdinalIgnoreCase))
            {
                return CommandIntentKind.OpenExistingFile;
            }

            return CommandIntentKind.VerifyFileExists;
        }

        if (resolvedTargets.Any(target => target.Kind == TargetKind.Window))
        {
            if (LooksLikeForegroundVerificationIntent(normalizedInput))
            {
                return CommandIntentKind.VerifyForegroundAlignment;
            }

            return CommandIntentKind.FocusWindow;
        }

        var hasProcessLikeTarget = resolvedTargets.Any(target =>
            target.Kind is TargetKind.Process or TargetKind.Application);

        var hasExecutableGrounding = grounding is not null &&
            grounding.Disposition == TargetGroundingDisposition.Resolved &&
            grounding.ExecutionSuitability == TargetExecutionSuitability.ExecutableHere &&
            grounding.Target.Kind is GroundedTargetKind.KnownApplication or GroundedTargetKind.PathLike;

        if (hasProcessLikeTarget || hasExecutableGrounding)
        {
            if (LooksLikeRunningVerificationIntent(normalizedInput))
            {
                return CommandIntentKind.VerifyProcessRunning;
            }

            if (LooksLikeForegroundVerificationIntent(normalizedInput))
            {
                return CommandIntentKind.VerifyForegroundAlignment;
            }

            if (normalizedInput.StartsWith("focus ", StringComparison.OrdinalIgnoreCase))
            {
                return CommandIntentKind.FocusWindow;
            }

            if (LooksLikeOpenApplicationIntent(normalizedInput))
            {
                return CommandIntentKind.OpenApplication;
            }
        }

        return MapIntentFromDecision(decision);
    }

    private static CommandIntentKind MapIntentFromDecision(AiDecision? decision)
    {
        if (decision is null)
        {
            return CommandIntentKind.Unknown;
        }

        var intentFromNextAction = MapIntentFromNextActionDecision(decision.NextActionDecision);
        if (intentFromNextAction.HasValue)
        {
            return intentFromNextAction.Value;
        }

        var selectedToolName = ResolveDecisionToolNameForDisplay(decision);

        if (selectedToolName.Equals("OpenAppTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.OpenApplication;
        }

        if (selectedToolName.Equals("FocusWindowTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.FocusWindow;
        }

        if (selectedToolName.Equals("VerifyProcessTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.VerifyProcessRunning;
        }

        if (selectedToolName.Equals("VerifyForegroundAlignmentTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.VerifyForegroundAlignment;
        }

        if (selectedToolName.Equals("VerifyServiceStatusTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.VerifyServiceStatus;
        }

        if (selectedToolName.Equals("StartServiceTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.StartService;
        }

        if (selectedToolName.Equals("StopServiceTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.StopService;
        }

        if (selectedToolName.Equals("VerifyFileExistsTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.VerifyFileExists;
        }

        if (selectedToolName.Equals("OpenExistingFileTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.OpenExistingFile;
        }

        if (selectedToolName.Equals("TypeTextTool", StringComparison.OrdinalIgnoreCase) ||
            selectedToolName.Equals("PressKeyTool", StringComparison.OrdinalIgnoreCase) ||
            selectedToolName.Equals("PressShortcutTool", StringComparison.OrdinalIgnoreCase))
        {
            return CommandIntentKind.PrimitiveInput;
        }

        return CommandIntentKind.Unknown;
    }

    private static CommandIntentKind? MapIntentFromNextActionDecision(NextActionDecision? nextActionDecision)
    {
        if (nextActionDecision is null ||
            nextActionDecision.Kind != DecisionKind.ExecuteAction ||
            nextActionDecision.ExecuteAction is null)
        {
            return null;
        }

        return nextActionDecision.ExecuteAction.ActionType switch
        {
            ActionType.Launch => CommandIntentKind.OpenApplication,
            ActionType.Focus => CommandIntentKind.FocusWindow,
            ActionType.Verify => CommandIntentKind.VerifyProcessRunning,
            ActionType.OpenFile => CommandIntentKind.OpenExistingFile,
            ActionType.InputText => CommandIntentKind.PrimitiveInput,
            ActionType.Confirm => CommandIntentKind.PrimitiveInput,
            ActionType.Cancel => CommandIntentKind.PrimitiveInput,
            ActionType.PressKey => CommandIntentKind.PrimitiveInput,
            ActionType.PressShortcut => CommandIntentKind.PrimitiveInput,
            _ => null
        };
    }

    private static bool LooksLikePrimitiveInputIntent(string normalizedInput)
    {
        return normalizedInput.StartsWith("type ", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("write ", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("press key ", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("key ", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("press shortcut ", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("shortcut ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOpenApplicationIntent(string normalizedInput)
    {
        return normalizedInput.StartsWith("open ", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("launch ", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("run ", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("start ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeRunningVerificationIntent(string normalizedInput)
    {
        return normalizedInput.StartsWith("is ", StringComparison.OrdinalIgnoreCase) &&
                   normalizedInput.EndsWith(" running", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("check ", StringComparison.OrdinalIgnoreCase) &&
                   normalizedInput.EndsWith(" running", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.StartsWith("verify ", StringComparison.OrdinalIgnoreCase) &&
                   normalizedInput.EndsWith(" running", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeForegroundVerificationIntent(string normalizedInput)
    {
        return normalizedInput.EndsWith(" foreground", StringComparison.OrdinalIgnoreCase) ||
               normalizedInput.EndsWith(" active", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractWindowHandle(TargetReference target, out long windowHandle)
    {
        windowHandle = 0;

        if (long.TryParse(target.NormalizedValue, out var parsedNormalized) && parsedNormalized > 0)
        {
            windowHandle = parsedNormalized;
            return true;
        }

        if (target.Metadata is null)
        {
            return false;
        }

        if (target.Metadata.TryGetValue("windowHandle", out var metadataHandle) &&
            long.TryParse(metadataHandle, out var parsedMetadata) &&
            parsedMetadata > 0)
        {
            windowHandle = parsedMetadata;
            return true;
        }

        return false;
    }

    private static bool IsCapabilitySuccessful(ExecutionStatus status)
    {
        return status is ExecutionStatus.Succeeded or ExecutionStatus.Attempted or ExecutionStatus.PartiallySucceeded;
    }

    private static string DetermineCapabilityExecutionMode(ActionExecutionResult result)
    {
        return result.Status switch
        {
            ExecutionStatus.Succeeded => "real",
            ExecutionStatus.Attempted => "real",
            ExecutionStatus.PartiallySucceeded => "real",
            ExecutionStatus.Blocked => "blocked",
            ExecutionStatus.Skipped => "simulated",
            _ => "unknown"
        };
    }

    private async Task<(ActionExecutionResult Result, AgentExecutionContext Context)> ApplyVerificationFeedbackAsync(
        CommandRequest request,
        SafetyDisposition safety,
        SafetyRiskLevel riskLevel,
        AgentAction action,
        ActionType? actionType,
        ActionExecutionResult result,
        AgentExecutionContext context,
        CancellationToken cancellationToken,
        IDictionary<string, string> metadata)
    {
        if (_executionVerifier is null)
        {
            return (result, context);
        }

        if (!IsCapabilitySuccessful(result.Status))
        {
            return (result, context);
        }

        var verificationPolicy = ActionVerificationPolicyResolver.Resolve(actionType);
        if (verificationPolicy.Mode != ActionVerificationMode.StrictExternal)
        {
            return (result, context);
        }

        if (!TryBuildVerificationSpecifications(action, verificationPolicy, out var specifications, out var specificationFailureReason))
        {
            var unsupportedVerification = CreatePolicyVerificationResult(
                verificationPolicy,
                VerificationStatus.Unsupported,
                specificationFailureReason ?? "Strict verification could not build a compatible specification.");
            EnrichMetadataWithVerification(metadata, unsupportedVerification);
            var unsupportedResult = AttachVerificationResult(result, unsupportedVerification, failClosedForStrictVerification: true);
            return (unsupportedResult, context);
        }

        var verificationContext = context;
        try
        {
            var postObservation = await _observationProvider.CaptureAsync(cancellationToken);
            var postRichObservation = await CaptureCommandObservationAsync(
                new CommandObservationRequest
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    OriginalUserCommand = request.UserInput,
                    BaselineObservation = postObservation,
                    PrimaryTargetGrounding = context.PrimaryTargetGrounding,
                    SafetyDisposition = safety,
                    SafetyRiskLevel = riskLevel
                },
                cancellationToken);

            verificationContext = new AgentExecutionContext
            {
                CorrelationId = context.CorrelationId,
                RawInput = context.RawInput,
                NormalizedInput = context.NormalizedInput,
                DetectedIntent = context.DetectedIntent,
                CreatedAtUtc = context.CreatedAtUtc,
                SessionId = context.SessionId,
                Observation = postObservation,
                RichObservation = postRichObservation,
                ContextAdapter = context.ContextAdapter,
                PrimaryTargetGrounding = context.PrimaryTargetGrounding,
                RuntimeState = context.RuntimeState,
                ResolvedTargets = context.ResolvedTargets,
                Metadata = context.Metadata
            };
        }
        catch
        {
            verificationContext = context;
        }

        var verificationRequest = new VerificationRequest
        {
            CorrelationId = context.CorrelationId.ToString("N"),
            Action = action,
            ExecutionResult = result,
            ExecutionContext = verificationContext,
            Specifications = specifications,
            RequestedAtUtc = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executionPath"] = "capability",
                ["capability"] = action.CapabilityName,
                ["action"] = action.ActionName
            }
        };

        var verification = await _executionVerifier.VerifyAsync(verificationRequest, cancellationToken);
        EnrichMetadataWithVerification(metadata, verification);
        var enrichedResult = AttachVerificationResult(
            result,
            verification,
            failClosedForStrictVerification: verificationPolicy.Mode == ActionVerificationMode.StrictExternal);
        return (enrichedResult, verificationContext);
    }

    private static bool TryBuildVerificationSpecifications(
        AgentAction action,
        ActionVerificationPolicy verificationPolicy,
        out IReadOnlyList<VerificationPrimitiveSpec> specifications,
        out string? failureReason)
    {
        specifications = [];
        failureReason = null;

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.ProcessPresence)
        {
            if (!TryResolveExpectedProcessName(action, out var expectedProcessName))
            {
                failureReason = "Process verification requires an expected process name, but target metadata did not provide one.";
                return false;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.ProcessPresence,
                    Required = true,
                    Description = "Verify expected process is present after execution.",
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["expectedProcessName"] = expectedProcessName
                    }
                }
            ];

            return true;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.ForegroundAlignment)
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (TryResolveExpectedWindowHandle(action, out var expectedWindowHandle))
            {
                parameters["expectedWindowHandle"] = expectedWindowHandle.ToString();
            }

            if (TryResolveExpectedProcessName(action, out var expectedProcessName))
            {
                parameters["expectedProcessName"] = expectedProcessName;
            }

            if (parameters.Count == 0)
            {
                failureReason = "Foreground verification requires either a process name or window handle target.";
                return false;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.ForegroundAlignment,
                    Required = true,
                    Description = "Verify target focus/alignment after execution.",
                    Parameters = parameters
                }
            ];

            return true;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.FileOpenPostcondition)
        {
            if (!TryResolveExpectedFilePath(action, out var expectedFilePath))
            {
                failureReason = "File-open verification requires an expected file path target.";
                return false;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.FileOpenPostcondition,
                    Required = true,
                    Description = "Verify expected file-open postcondition on foreground context.",
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["expectedFilePath"] = expectedFilePath
                    }
                }
            ];

            return true;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.TextInputPostcondition)
        {
            if (!TryResolveExpectedInputText(action, out var expectedText))
            {
                failureReason = "Text-input verification requires an expected text payload.";
                return false;
            }

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["expectedText"] = expectedText
            };

            var targetHint = ResolveAgentActionTargetHint(action);
            if (!string.IsNullOrWhiteSpace(targetHint) &&
                !targetHint.Equals("active-context", StringComparison.OrdinalIgnoreCase))
            {
                parameters["expectedTargetHint"] = targetHint;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.TextInputPostcondition,
                    Required = true,
                    Description = "Verify typed text is observable in post-action context with target-bound hints.",
                    Parameters = parameters
                }
            ];
            return true;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.KeyInteractionPostcondition)
        {
            var semantic = ResolveKeyInteractionSemanticFromActionType(action.ActionName);
            if (string.IsNullOrWhiteSpace(semantic))
            {
                failureReason = "Key-interaction verification requires a known action semantic.";
                return false;
            }

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["interactionSemantic"] = semantic
            };

            var expectedTargetHint = ResolveAgentActionTargetHint(action);
            if (!string.IsNullOrWhiteSpace(expectedTargetHint) &&
                !expectedTargetHint.Equals("active-context", StringComparison.OrdinalIgnoreCase))
            {
                parameters["expectedTargetHint"] = expectedTargetHint;
            }

            if (action.Parameters is not null &&
                action.Parameters.TryGetValue("key", out var key) &&
                !string.IsNullOrWhiteSpace(key))
            {
                parameters["expectedKey"] = key.Trim();
            }

            if (action.Parameters is not null &&
                action.Parameters.TryGetValue("shortcut", out var shortcut) &&
                !string.IsNullOrWhiteSpace(shortcut))
            {
                parameters["expectedShortcut"] = shortcut.Trim();
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.KeyInteractionPostcondition,
                    Required = true,
                    Description = "Verify key interaction produced action-specific postcondition evidence.",
                    Parameters = parameters
                }
            ];
            return true;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.NavigationDestination)
        {
            if (!TryResolveExpectedNavigationTarget(action, out var expectedDestination))
            {
                failureReason = "Navigation verification requires an expected destination hint.";
                return false;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.NavigationDestination,
                    Required = true,
                    Description = "Verify navigation reached expected destination context.",
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["expectedDestination"] = expectedDestination
                    }
                }
            ];

            return true;
        }

        failureReason = $"Strict verification policy '{verificationPolicy.VerificationKind}' is not supported by the specification builder.";
        return false;
    }

    private static bool TryBuildVerificationSpecifications(
        ExecuteActionPayload? executeAction,
        ActionVerificationPolicy verificationPolicy,
        out IReadOnlyList<VerificationPrimitiveSpec> specifications,
        out string? failureReason)
    {
        specifications = [];
        failureReason = null;

        if (executeAction is null)
        {
            failureReason = "Strict verification requires an execute action payload.";
            return false;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.FileOpenPostcondition)
        {
            var expectedFilePath = ResolveExecuteActionExpectedValue(
                executeAction,
                fallbackReference: executeAction.Target.Reference,
                "path",
                "file",
                "target");

            if (string.IsNullOrWhiteSpace(expectedFilePath))
            {
                failureReason = "File-open verification requires an expected file path parameter.";
                return false;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.FileOpenPostcondition,
                    Required = true,
                    Description = "Verify expected file-open postcondition on foreground context.",
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["expectedFilePath"] = expectedFilePath
                    }
                }
            ];
            return true;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.NavigationDestination)
        {
            var expectedDestination = ResolveExecuteActionExpectedValue(
                executeAction,
                fallbackReference: executeAction.Target.Reference,
                "destination",
                "route",
                "target",
                "context");

            if (string.IsNullOrWhiteSpace(expectedDestination))
            {
                failureReason = "Navigation verification requires an expected destination parameter.";
                return false;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.NavigationDestination,
                    Required = true,
                    Description = "Verify navigation reached expected destination context.",
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["expectedDestination"] = expectedDestination
                    }
                }
            ];
            return true;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.TextInputPostcondition)
        {
            var expectedText = ResolveExecuteActionExpectedValue(
                executeAction,
                fallbackReference: null,
                "text",
                "input",
                "value");

            if (string.IsNullOrWhiteSpace(expectedText))
            {
                failureReason = "Text-input verification requires an expected text parameter.";
                return false;
            }

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["expectedText"] = expectedText
            };

            if (!string.IsNullOrWhiteSpace(executeAction.Target.Reference) &&
                !executeAction.Target.Reference.Equals("active-context", StringComparison.OrdinalIgnoreCase))
            {
                parameters["expectedTargetHint"] = executeAction.Target.Reference;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.TextInputPostcondition,
                    Required = true,
                    Description = "Verify typed text is observable in post-action context with target-bound hints.",
                    Parameters = parameters
                }
            ];
            return true;
        }

        if (verificationPolicy.VerificationKind == DecisionVerificationKind.KeyInteractionPostcondition)
        {
            var semantic = ResolveKeyInteractionSemantic(executeAction.ActionType);
            if (string.IsNullOrWhiteSpace(semantic))
            {
                failureReason = "Key-interaction verification requires a known action semantic.";
                return false;
            }

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["interactionSemantic"] = semantic
            };

            if (!string.IsNullOrWhiteSpace(executeAction.Target.Reference) &&
                !executeAction.Target.Reference.Equals("active-context", StringComparison.OrdinalIgnoreCase))
            {
                parameters["expectedTargetHint"] = executeAction.Target.Reference;
            }

            var expectedKey = ResolveExecuteActionExpectedValue(executeAction, fallbackReference: null, "key", "value");
            if (!string.IsNullOrWhiteSpace(expectedKey))
            {
                parameters["expectedKey"] = expectedKey;
            }

            var expectedShortcut = ResolveExecuteActionExpectedValue(executeAction, fallbackReference: null, "shortcut", "value");
            if (!string.IsNullOrWhiteSpace(expectedShortcut))
            {
                parameters["expectedShortcut"] = expectedShortcut;
            }

            specifications =
            [
                new VerificationPrimitiveSpec
                {
                    Kind = VerificationPrimitiveKind.KeyInteractionPostcondition,
                    Required = true,
                    Description = "Verify key interaction produced action-specific postcondition evidence.",
                    Parameters = parameters
                }
            ];
            return true;
        }

        failureReason = $"Strict verification policy '{verificationPolicy.VerificationKind}' is not supported for tool-path payload verification.";
        return false;
    }

    private static bool TryResolveExpectedProcessName(AgentAction action, out string expectedProcessName)
    {
        expectedProcessName = string.Empty;

        if (action.Target?.Metadata is not null &&
            action.Target.Metadata.TryGetValue("processName", out var processNameFromMetadata) &&
            !string.IsNullOrWhiteSpace(processNameFromMetadata))
        {
            expectedProcessName = NormalizeProcessName(processNameFromMetadata);
            return !string.IsNullOrWhiteSpace(expectedProcessName);
        }

        if (!string.IsNullOrWhiteSpace(action.Target?.NormalizedValue) &&
            !long.TryParse(action.Target.NormalizedValue, out _))
        {
            expectedProcessName = NormalizeProcessName(action.Target.NormalizedValue);
            return !string.IsNullOrWhiteSpace(expectedProcessName);
        }

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("app", out var appParameter) &&
            !string.IsNullOrWhiteSpace(appParameter))
        {
            expectedProcessName = NormalizeProcessName(appParameter);
            return !string.IsNullOrWhiteSpace(expectedProcessName);
        }

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("process", out var processParameter) &&
            !string.IsNullOrWhiteSpace(processParameter))
        {
            expectedProcessName = NormalizeProcessName(processParameter);
            return !string.IsNullOrWhiteSpace(expectedProcessName);
        }

        return false;
    }

    private static bool TryResolveExpectedInputText(AgentAction action, out string expectedText)
    {
        expectedText = string.Empty;

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("text", out var textParameter) &&
            !string.IsNullOrWhiteSpace(textParameter))
        {
            expectedText = textParameter.Trim();
            return true;
        }

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("input", out var inputParameter) &&
            !string.IsNullOrWhiteSpace(inputParameter))
        {
            expectedText = inputParameter.Trim();
            return true;
        }

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("value", out var valueParameter) &&
            !string.IsNullOrWhiteSpace(valueParameter))
        {
            expectedText = valueParameter.Trim();
            return true;
        }

        return false;
    }

    private static string ResolveAgentActionTargetHint(AgentAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.Target?.NormalizedValue))
        {
            return action.Target.NormalizedValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(action.Target?.DisplayName))
        {
            return action.Target.DisplayName.Trim();
        }

        return action.Target?.OriginalText?.Trim() ?? string.Empty;
    }

    private static bool TryResolveExpectedFilePath(AgentAction action, out string expectedFilePath)
    {
        expectedFilePath = string.Empty;

        if (action.Target?.Metadata is not null &&
            action.Target.Metadata.TryGetValue("filePath", out var filePathFromMetadata) &&
            !string.IsNullOrWhiteSpace(filePathFromMetadata))
        {
            expectedFilePath = NormalizeFilePath(filePathFromMetadata);
            return !string.IsNullOrWhiteSpace(expectedFilePath);
        }

        if (!string.IsNullOrWhiteSpace(action.Target?.NormalizedValue))
        {
            expectedFilePath = NormalizeFilePath(action.Target.NormalizedValue);
            return !string.IsNullOrWhiteSpace(expectedFilePath);
        }

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("path", out var pathParameter) &&
            !string.IsNullOrWhiteSpace(pathParameter))
        {
            expectedFilePath = NormalizeFilePath(pathParameter);
            return !string.IsNullOrWhiteSpace(expectedFilePath);
        }

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("file", out var fileParameter) &&
            !string.IsNullOrWhiteSpace(fileParameter))
        {
            expectedFilePath = NormalizeFilePath(fileParameter);
            return !string.IsNullOrWhiteSpace(expectedFilePath);
        }

        return false;
    }

    private static bool TryResolveExpectedNavigationTarget(AgentAction action, out string expectedDestination)
    {
        expectedDestination = string.Empty;

        if (action.Target?.Metadata is not null &&
            action.Target.Metadata.TryGetValue("destination", out var destinationFromMetadata) &&
            !string.IsNullOrWhiteSpace(destinationFromMetadata))
        {
            expectedDestination = destinationFromMetadata.Trim();
            return true;
        }

        if (!string.IsNullOrWhiteSpace(action.Target?.NormalizedValue))
        {
            expectedDestination = action.Target.NormalizedValue.Trim();
            return true;
        }

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("route", out var routeParameter) &&
            !string.IsNullOrWhiteSpace(routeParameter))
        {
            expectedDestination = routeParameter.Trim();
            return true;
        }

        if (action.Parameters is not null &&
            action.Parameters.TryGetValue("destination", out var destinationParameter) &&
            !string.IsNullOrWhiteSpace(destinationParameter))
        {
            expectedDestination = destinationParameter.Trim();
            return true;
        }

        return false;
    }

    private static string ResolveExecuteActionExpectedValue(
        ExecuteActionPayload executeAction,
        string? fallbackReference,
        params string[] preferredParameterKeys)
    {
        foreach (var preferredKey in preferredParameterKeys)
        {
            foreach (var pair in executeAction.Parameters.Values)
            {
                if (pair.Key.Equals(preferredKey, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Value))
                {
                    return pair.Value.Trim();
                }
            }
        }

        return string.IsNullOrWhiteSpace(fallbackReference)
            ? string.Empty
            : fallbackReference.Trim();
    }

    private static string ResolveKeyInteractionSemantic(ActionType actionType)
    {
        return actionType switch
        {
            ActionType.Confirm => "confirm",
            ActionType.Cancel => "cancel",
            ActionType.PressKey => "presskey",
            ActionType.PressShortcut => "pressshortcut",
            _ => string.Empty
        };
    }

    private static string ResolveKeyInteractionSemanticFromActionType(string actionName)
    {
        if (actionName.Equals("Confirm", StringComparison.OrdinalIgnoreCase))
        {
            return "confirm";
        }

        if (actionName.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
        {
            return "cancel";
        }

        if (actionName.Equals("PressKey", StringComparison.OrdinalIgnoreCase))
        {
            return "presskey";
        }

        if (actionName.Equals("PressShortcut", StringComparison.OrdinalIgnoreCase))
        {
            return "pressshortcut";
        }

        return string.Empty;
    }

    private static Dictionary<string, string> BuildPreObservationMetadata(ObservationSnapshot? preObservation)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (preObservation is null)
        {
            return metadata;
        }

        if (!string.IsNullOrWhiteSpace(preObservation.ActiveProcessName))
        {
            metadata["preActiveProcess"] = preObservation.ActiveProcessName;
        }

        if (!string.IsNullOrWhiteSpace(preObservation.ActiveWindow?.Title))
        {
            metadata["preActiveWindowTitle"] = preObservation.ActiveWindow.Title;
        }

        if (!string.IsNullOrWhiteSpace(preObservation.SelectionTextPreview))
        {
            metadata["preSelectionText"] = preObservation.SelectionTextPreview;
        }

        if (!string.IsNullOrWhiteSpace(preObservation.ClipboardTextPreview))
        {
            metadata["preClipboardText"] = preObservation.ClipboardTextPreview;
        }

        return metadata;
    }

    private static string NormalizeFilePath(string filePath)
    {
        return filePath.Trim().Trim('"', '\'');
    }

    private static string NormalizeProcessName(string processName)
    {
        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.ToLowerInvariant();
    }

    private static bool TryResolveExpectedWindowHandle(AgentAction action, out long expectedWindowHandle)
    {
        expectedWindowHandle = 0;

        if (action.Target is null)
        {
            return false;
        }

        if (long.TryParse(action.Target.NormalizedValue, out var normalizedWindowHandle) &&
            normalizedWindowHandle > 0)
        {
            expectedWindowHandle = normalizedWindowHandle;
            return true;
        }

        if (action.Target.Metadata is not null &&
            action.Target.Metadata.TryGetValue("windowHandle", out var metadataWindowHandle) &&
            long.TryParse(metadataWindowHandle, out var parsedMetadataWindowHandle) &&
            parsedMetadataWindowHandle > 0)
        {
            expectedWindowHandle = parsedMetadataWindowHandle;
            return true;
        }

        return false;
    }

    private static VerificationResult CreatePolicyVerificationResult(
        ActionVerificationPolicy policy,
        VerificationStatus status,
        string reason)
    {
        var primitiveKind = policy.VerificationKind switch
        {
            DecisionVerificationKind.ProcessPresence => VerificationPrimitiveKind.ProcessPresence,
            DecisionVerificationKind.ForegroundAlignment => VerificationPrimitiveKind.ForegroundAlignment,
            DecisionVerificationKind.TextInputPostcondition => VerificationPrimitiveKind.TextInputPostcondition,
            DecisionVerificationKind.KeyInteractionPostcondition => VerificationPrimitiveKind.KeyInteractionPostcondition,
            DecisionVerificationKind.FileOpenPostcondition => VerificationPrimitiveKind.FileOpenPostcondition,
            DecisionVerificationKind.NavigationDestination => VerificationPrimitiveKind.NavigationDestination,
            _ => VerificationPrimitiveKind.ProcessPresence
        };

        return new VerificationResult
        {
            Status = status,
            Reason = reason,
            PrimitiveResults =
            [
                new VerificationPrimitiveResult
                {
                    PrimitiveKind = primitiveKind,
                    Status = status,
                    Reason = reason,
                    Evidence =
                    [
                        new VerificationEvidence
                        {
                            Code = status == VerificationStatus.Unsupported
                                ? "verification_spec_unsupported"
                                : "verification_spec_inconclusive",
                            Message = reason,
                            Data = null
                        }
                    ],
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                }
            ],
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ActionExecutionResult AttachVerificationResult(
        ActionExecutionResult result,
        VerificationResult verification,
        bool failClosedForStrictVerification = false)
    {
        var status = result.Status;
        var message = result.Message;

        if (verification.Status == VerificationStatus.NotVerified && IsCapabilitySuccessful(result.Status))
        {
            status = ExecutionStatus.VerificationFailed;
            message = $"{result.Message} Verification failed: {verification.Reason}";
        }
        else if (verification.Status == VerificationStatus.Inconclusive)
        {
            if (failClosedForStrictVerification && IsCapabilitySuccessful(result.Status))
            {
                status = ExecutionStatus.Blocked;
            }

            message = $"{result.Message} Verification inconclusive: {verification.Reason}";
        }
        else if (verification.Status == VerificationStatus.Unsupported)
        {
            if (failClosedForStrictVerification && IsCapabilitySuccessful(result.Status))
            {
                status = ExecutionStatus.Blocked;
            }

            message = failClosedForStrictVerification
                ? $"{result.Message} Verification unsupported: {verification.Reason}"
                : $"{result.Message} Verification skipped: {verification.Reason}";
        }

        return new ActionExecutionResult
        {
            Status = status,
            Message = message,
            IsVerified = verification.Status == VerificationStatus.Verified,
            OutputText = result.OutputText,
            OutputData = result.OutputData,
            ErrorCode = result.ErrorCode,
            UsedFallback = result.UsedFallback,
            PrimitiveExecution = result.PrimitiveExecution,
            Verification = verification,
            StartedAtUtc = result.StartedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc
        };
    }

    private static AgentExecutionContext ApplyRuntimeState(
        AgentExecutionContext context,
        ExecutionRuntimeState runtimeState)
    {
        var metadata = context.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(context.Metadata, StringComparer.OrdinalIgnoreCase);

        EnrichMetadataWithRuntimeState(metadata, runtimeState);

        CommandObservationSnapshot? richObservation = null;
        if (context.RichObservation is not null)
        {
            richObservation = new CommandObservationSnapshot
            {
                TimestampUtc = context.RichObservation.TimestampUtc,
                OriginalUserCommand = context.RichObservation.OriginalUserCommand,
                ForegroundWindow = context.RichObservation.ForegroundWindow,
                ForegroundProcess = context.RichObservation.ForegroundProcess,
                GroundingSummary = context.RichObservation.GroundingSummary,
                SafetySummary = context.RichObservation.SafetySummary,
                RuntimeState = runtimeState,
                CollectionStatus = context.RichObservation.CollectionStatus,
                CollectionMessage = context.RichObservation.CollectionMessage
            };
        }

        return new AgentExecutionContext
        {
            CorrelationId = context.CorrelationId,
            RawInput = context.RawInput,
            NormalizedInput = context.NormalizedInput,
            DetectedIntent = context.DetectedIntent,
            CreatedAtUtc = context.CreatedAtUtc,
            SessionId = context.SessionId,
            Observation = context.Observation,
            RichObservation = richObservation ?? context.RichObservation,
            ContextAdapter = context.ContextAdapter,
            PrimaryTargetGrounding = context.PrimaryTargetGrounding,
            RuntimeState = runtimeState,
            ResolvedTargets = context.ResolvedTargets,
            Metadata = metadata
        };
    }

    private static AgentExecutionContext ApplyObservationRuntimeTransitionMetadata(
        AgentExecutionContext context,
        DecisionLoopPostExecutionTransition transition)
    {
        var metadata = context.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(context.Metadata, StringComparer.OrdinalIgnoreCase);

        metadata["observation_runtime_consumed"] = transition.ObservationRuntimeConsumed ? "true" : "false";
        metadata["observation_runtime_conflict"] = transition.ObservationRuntimeConflict ? "true" : "false";
        metadata["observation_runtime_override_applied"] = transition.ObservationRuntimeOverrideApplied ? "true" : "false";
        metadata["observation_runtime_policy_mode"] = transition.ObservationRuntimePolicyMode;

        if (!string.IsNullOrWhiteSpace(transition.ObservationRuntimeConflictType))
        {
            metadata["observation_runtime_conflict_type"] = transition.ObservationRuntimeConflictType;
        }

        if (!string.IsNullOrWhiteSpace(transition.ObservationRuntimeDecisionBasis))
        {
            metadata["observation_runtime_decision_basis"] = transition.ObservationRuntimeDecisionBasis;
        }

        return new AgentExecutionContext
        {
            CorrelationId = context.CorrelationId,
            RawInput = context.RawInput,
            NormalizedInput = context.NormalizedInput,
            DetectedIntent = context.DetectedIntent,
            CreatedAtUtc = context.CreatedAtUtc,
            SessionId = context.SessionId,
            Observation = context.Observation,
            RichObservation = context.RichObservation,
            ContextAdapter = context.ContextAdapter,
            PrimaryTargetGrounding = context.PrimaryTargetGrounding,
            RuntimeState = context.RuntimeState,
            ResolvedTargets = context.ResolvedTargets,
            Metadata = metadata
        };
    }

    private static ExecutionRuntimeState BuildRuntimeState(
        AgentAction action,
        ActionExecutionResult result,
        TargetGroundingResult? grounding)
    {
        var blockedReason = result.Status == ExecutionStatus.Blocked
            ? result.Message
            : result.PrimitiveExecution?.BlockedReason;

        var failureReason = result.Status is ExecutionStatus.Failed or ExecutionStatus.VerificationFailed
            ? result.Message
            : null;

        var targetKind = action.Target?.Kind;
        if (!targetKind.HasValue && grounding is not null &&
            grounding.Target.Kind is GroundedTargetKind.KnownApplication or GroundedTargetKind.PathLike)
        {
            targetKind = TargetKind.Application;
        }

        var targetValue = !string.IsNullOrWhiteSpace(action.Target?.NormalizedValue)
            ? action.Target!.NormalizedValue
            : grounding?.Target.CanonicalValue;

        return new ExecutionRuntimeState
        {
            CapabilityName = action.CapabilityName,
            ActionName = action.ActionName,
            ActionStatus = result.Status,
            VerificationStatus = result.Verification?.Status,
            VerificationReason = result.Verification?.Reason,
            TargetKind = targetKind,
            TargetValue = targetValue,
            BlockedReason = blockedReason,
            FailureReason = failureReason,
            PrimitiveKind = result.PrimitiveExecution?.PrimitiveKind,
            PrimitiveSucceeded = result.PrimitiveExecution?.Success
        };
    }

    private static ExecutionRuntimeState BuildRuntimeStateFromToolResult(string toolName, ToolResult toolResult)
    {
        var actionStatus = toolResult.Success
            ? ExecutionStatus.Succeeded
            : string.IsNullOrWhiteSpace(toolResult.PrimitiveExecution?.BlockedReason)
                ? ExecutionStatus.Failed
                : ExecutionStatus.Blocked;

        return new ExecutionRuntimeState
        {
            CapabilityName = $"Tool:{toolName}",
            ActionName = "ExecuteTool",
            ActionStatus = actionStatus,
            VerificationStatus = null,
            VerificationReason = null,
            TargetKind = null,
            TargetValue = null,
            BlockedReason = toolResult.PrimitiveExecution?.BlockedReason,
            FailureReason = toolResult.Success ? null : toolResult.Message,
            PrimitiveKind = toolResult.PrimitiveExecution?.PrimitiveKind,
            PrimitiveSucceeded = toolResult.PrimitiveExecution?.Success
        };
    }

    private static void EnrichMetadataWithRuntimeState(
        IDictionary<string, string> metadata,
        ExecutionRuntimeState runtimeState)
    {
        metadata["runtime_state_available"] = "true";

        if (!string.IsNullOrWhiteSpace(runtimeState.CapabilityName))
        {
            metadata["runtime_capability"] = runtimeState.CapabilityName;
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.ActionName))
        {
            metadata["runtime_action"] = runtimeState.ActionName;
        }

        if (runtimeState.ActionStatus.HasValue)
        {
            metadata["runtime_action_status"] = runtimeState.ActionStatus.Value.ToString();
        }

        if (runtimeState.VerificationStatus.HasValue)
        {
            metadata["runtime_verification_status"] = runtimeState.VerificationStatus.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.VerificationReason))
        {
            metadata["runtime_verification_reason"] = runtimeState.VerificationReason;
        }

        if (runtimeState.TargetKind.HasValue)
        {
            metadata["runtime_target_kind"] = runtimeState.TargetKind.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.TargetValue))
        {
            metadata["runtime_target_value"] = runtimeState.TargetValue;
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.BlockedReason))
        {
            metadata["runtime_blocked_reason"] = runtimeState.BlockedReason;
        }

        if (!string.IsNullOrWhiteSpace(runtimeState.FailureReason))
        {
            metadata["runtime_failure_reason"] = runtimeState.FailureReason;
        }

        if (runtimeState.PrimitiveKind.HasValue)
        {
            metadata["runtime_primitive_kind"] = runtimeState.PrimitiveKind.Value.ToString();
        }

        if (runtimeState.PrimitiveSucceeded.HasValue)
        {
            metadata["runtime_primitive_success"] = runtimeState.PrimitiveSucceeded.Value ? "true" : "false";
        }
    }

    private static void EnrichMetadataWithVerification(
        IDictionary<string, string> metadata,
        VerificationResult verification)
    {
        metadata["verification_used"] = "true";
        metadata["verification_status"] = verification.Status.ToString();
        metadata["verification_reason"] = verification.Reason;
        metadata["verification_primitive_count"] = verification.PrimitiveResults.Count.ToString();
    }

    private static bool TryGetVerifiedServiceRunningState(ActionExecutionResult result, out bool isRunning)
    {
        isRunning = false;

        if (result.OutputData is null)
        {
            return false;
        }

        if (!result.OutputData.TryGetValue("serviceExists", out var serviceExistsText) ||
            !TryParseBooleanMetadata(serviceExistsText, out var serviceExists) ||
            !serviceExists)
        {
            return false;
        }

        if (!result.OutputData.TryGetValue("serviceRunning", out var serviceRunningText) ||
            !TryParseBooleanMetadata(serviceRunningText, out var parsedRunning))
        {
            return false;
        }

        isRunning = parsedRunning;
        return true;
    }

    private static bool TryParseBooleanMetadata(string? value, out bool parsedValue)
    {
        parsedValue = false;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out parsedValue);
    }

    private static IDictionary<string, string> CreateExecutionPathMetadata(
        string executionPath,
        string? capabilityName = null,
        bool? snapshotUsed = null,
        bool? snapshotFallback = null,
        bool? targetReasonNormalized = null,
        string? targetReasonSource = null,
        bool? snapshotContextUsed = null,
        bool? snapshotAdapterPreserved = null,
        bool? snapshotObservationPreserved = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["executionPath"] = executionPath
        };

        if (!string.IsNullOrWhiteSpace(capabilityName))
        {
            metadata["capability"] = capabilityName;
        }

        if (snapshotUsed.HasValue)
        {
            metadata["approvalSnapshotUsed"] = snapshotUsed.Value ? "true" : "false";
        }

        if (snapshotFallback.HasValue)
        {
            metadata["approvalSnapshotFallback"] = snapshotFallback.Value ? "true" : "false";
        }

        if (snapshotUsed == true && snapshotFallback == false)
        {
            metadata["approval_resume_mode"] = "snapshot";
        }

        if (targetReasonNormalized.HasValue)
        {
            metadata["target_reason_normalized"] = targetReasonNormalized.Value ? "true" : "false";
        }

        if (!string.IsNullOrWhiteSpace(targetReasonSource))
        {
            metadata["target_reason_source"] = targetReasonSource;
        }

        if (snapshotContextUsed.HasValue)
        {
            metadata["approval_snapshot_context_used"] = snapshotContextUsed.Value ? "true" : "false";
        }

        if (snapshotAdapterPreserved.HasValue)
        {
            metadata["approval_snapshot_adapter_preserved"] = snapshotAdapterPreserved.Value ? "true" : "false";
        }

        if (snapshotObservationPreserved.HasValue)
        {
            metadata["approval_snapshot_observation_preserved"] = snapshotObservationPreserved.Value ? "true" : "false";
        }

        return metadata;
    }

    private static void EnrichMetadataWithDecisionInput(
        IDictionary<string, string> metadata,
        DecisionInputBundle? decisionInputBundle)
    {
        metadata["decision_input_built"] = decisionInputBundle is not null ? "true" : "false";

        if (decisionInputBundle is null)
        {
            return;
        }

        metadata["decision_input_source"] = decisionInputBundle.Source == DecisionInputSource.ApprovedSnapshot
            ? "snapshot"
            : "live";
    }

    private static void EnrichMetadataWithDecisionSummary(
        IDictionary<string, string> metadata,
        DecisionSummary? decisionSummary)
    {
        metadata["decision_summary_built"] = decisionSummary is not null ? "true" : "false";

        if (decisionSummary is null)
        {
            return;
        }

        metadata["decision_summary_source"] = decisionSummary.Source == DecisionInputSource.ApprovedSnapshot
            ? "snapshot"
            : "live";

        if (decisionSummary.PrimaryTargetKind.HasValue)
        {
            metadata["decision_summary_primary_target_kind"] = decisionSummary.PrimaryTargetKind.Value.ToString();
        }

        if (decisionSummary.PrimaryTargetReason.HasValue)
        {
            metadata["decision_summary_primary_target_reason"] = decisionSummary.PrimaryTargetReason.Value.ToString();
        }
    }

    private static void EnrichMetadataWithAiDecisionInput(
        IDictionary<string, string> metadata,
        AiDecisionInput? aiDecisionInput)
    {
        metadata["ai_decision_input_built"] = aiDecisionInput is not null ? "true" : "false";

        if (aiDecisionInput is null)
        {
            return;
        }

        metadata["ai_decision_input_source"] = aiDecisionInput.Source == DecisionInputSource.ApprovedSnapshot
            ? "snapshot"
            : "live";

        if (aiDecisionInput.PrimaryTargetKind.HasValue)
        {
            metadata["ai_decision_input_primary_target_kind"] = aiDecisionInput.PrimaryTargetKind.Value.ToString();
        }

        if (aiDecisionInput.PrimaryTargetReason.HasValue)
        {
            metadata["ai_decision_input_primary_target_reason"] = aiDecisionInput.PrimaryTargetReason.Value.ToString();
        }
    }

    private static void EnrichMetadataWithNextActionDecision(
        IDictionary<string, string> metadata,
        AiDecision decision)
    {
        metadata["next_action_contract_present"] = decision.NextActionDecision is not null ? "true" : "false";

        var nextActionDecision = decision.NextActionDecision;
        if (nextActionDecision is null)
        {
            return;
        }

        metadata["next_action_kind"] = nextActionDecision.Kind.ToString();

        if (nextActionDecision.Confidence.HasValue)
        {
            metadata["next_action_confidence"] = nextActionDecision.Confidence.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.RequiresApprovalReason))
        {
            metadata["next_action_requires_approval_reason"] = nextActionDecision.RequiresApprovalReason;
            metadata["approval_reason"] = nextActionDecision.RequiresApprovalReason;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.RetryReason))
        {
            metadata["next_action_retry_reason"] = nextActionDecision.RetryReason;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.StopReason))
        {
            metadata["next_action_stop_reason"] = nextActionDecision.StopReason;
        }

        if (nextActionDecision.ExecuteAction is not null)
        {
            metadata["next_action_type"] = nextActionDecision.ExecuteAction.ActionType.ToString();
            metadata["next_action_target_kind"] = nextActionDecision.ExecuteAction.Target.Kind.ToString();
            metadata["action_type"] = nextActionDecision.ExecuteAction.ActionType.ToString();

            if (!string.IsNullOrWhiteSpace(nextActionDecision.ExecuteAction.Target.Reference))
            {
                metadata["next_action_target_reference"] = nextActionDecision.ExecuteAction.Target.Reference;
                metadata["action_target"] = nextActionDecision.ExecuteAction.Target.Reference;
            }
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.AskObserve?.ObservationRequest))
        {
            metadata["next_action_observation_request"] = nextActionDecision.AskObserve.ObservationRequest;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.AskObserve?.ObservationHint))
        {
            metadata["next_action_observation_hint"] = nextActionDecision.AskObserve.ObservationHint;
        }

        if (!string.IsNullOrWhiteSpace(nextActionDecision.AskApproval?.RequiresApprovalReason))
        {
            metadata["next_action_ask_approval_reason"] = nextActionDecision.AskApproval.RequiresApprovalReason;
            metadata["approval_reason"] = nextActionDecision.AskApproval.RequiresApprovalReason;
        }

        if (nextActionDecision.AskApproval?.ProposedAction is not null)
        {
            metadata["next_action_proposed_type"] = nextActionDecision.AskApproval.ProposedAction.ActionType.ToString();
            metadata["action_type"] = nextActionDecision.AskApproval.ProposedAction.ActionType.ToString();

            if (!string.IsNullOrWhiteSpace(nextActionDecision.AskApproval.ProposedAction.Target.Reference))
            {
                metadata["next_action_proposed_target"] = nextActionDecision.AskApproval.ProposedAction.Target.Reference;
                metadata["action_target"] = nextActionDecision.AskApproval.ProposedAction.Target.Reference;
            }
        }

        if (nextActionDecision.Retry?.RetryCountHint is int retryCountHint)
        {
            metadata["next_action_retry_count_hint"] = retryCountHint.ToString();
        }

        if (nextActionDecision.Stop is not null)
        {
            metadata["next_action_stop_disposition"] = nextActionDecision.Stop.Disposition.ToString();

            if (!string.IsNullOrWhiteSpace(nextActionDecision.Stop.StopReason))
            {
                metadata["next_action_stop_payload_reason"] = nextActionDecision.Stop.StopReason;
            }
        }
    }

    private static void EnrichMetadataWithPrimitiveExecution(
        IDictionary<string, string> metadata,
        ActionPrimitiveExecutionResult? primitiveExecution)
    {
        metadata["primitive_execution_scope"] = "tool-sub-seam";
        metadata["primitive_execution_used"] = primitiveExecution is not null ? "true" : "false";

        if (primitiveExecution is null)
        {
            return;
        }

        metadata["primitive_kind"] = primitiveExecution.PrimitiveKind.ToString();
        metadata["primitive_success"] = primitiveExecution.Success ? "true" : "false";
        metadata["primitive_outcome"] = primitiveExecution.Success
            ? "succeeded"
            : string.IsNullOrWhiteSpace(primitiveExecution.BlockedReason)
                ? "failed"
                : "blocked";
        metadata["primitive_started_at_utc"] = primitiveExecution.StartedAtUtc.ToString("O");
        metadata["primitive_completed_at_utc"] = primitiveExecution.CompletedAtUtc.ToString("O");

        if (!string.IsNullOrWhiteSpace(primitiveExecution.BlockedReason))
        {
            metadata["primitive_blocked_reason"] = primitiveExecution.BlockedReason;
        }

        if (!string.IsNullOrWhiteSpace(primitiveExecution.ErrorCode))
        {
            metadata["primitive_error_code"] = primitiveExecution.ErrorCode;
        }

        if (primitiveExecution.Metadata is null)
        {
            return;
        }

        foreach (var (key, value) in primitiveExecution.Metadata)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            metadata[$"primitive_{key}"] = value;
        }
    }

    private static void EnrichServiceIntentKindMetadata(
        IDictionary<string, string> metadata,
        AiDecision decision)
    {
        if (decision.SelectedToolName.Equals("VerifyServiceStatusTool", StringComparison.OrdinalIgnoreCase))
        {
            metadata["service_intent_kind"] = "VerifyServiceStatus";
            return;
        }

        if (decision.SelectedToolName.Equals("StartServiceTool", StringComparison.OrdinalIgnoreCase))
        {
            metadata["service_intent_kind"] = "StartService";
            return;
        }

        if (decision.SelectedToolName.Equals("StopServiceTool", StringComparison.OrdinalIgnoreCase))
        {
            metadata["service_intent_kind"] = "StopService";
        }
    }

    private static void EnrichFileIntentKindMetadata(
        IDictionary<string, string> metadata,
        AiDecision decision)
    {
        if (decision.SelectedToolName.Equals("VerifyFileExistsTool", StringComparison.OrdinalIgnoreCase))
        {
            metadata["file_intent_kind"] = "VerifyFileExists";
            return;
        }

        if (decision.SelectedToolName.Equals("OpenExistingFileTool", StringComparison.OrdinalIgnoreCase))
        {
            metadata["file_intent_kind"] = "OpenExistingFile";
        }
    }

    private static void EnrichWindowProcessCapabilityMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        TargetReference target)
    {
        if (!capabilityName.Equals("WindowProcessCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        metadata["window_process_capability_used"] = "true";
        metadata["focus_target_kind"] = target.Kind.ToString();
        metadata["focus_target_handle_available"] = TryExtractWindowHandle(target, out _) ? "true" : "false";
    }

    private static void EnrichWindowProcessCapabilityResultMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        ActionExecutionResult capabilityResult)
    {
        if (!capabilityName.Equals("WindowProcessCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (capabilityResult.OutputData is null)
        {
            return;
        }

        if (capabilityResult.OutputData.TryGetValue("focusTargetResolvedFrom", out var resolvedFrom) &&
            !string.IsNullOrWhiteSpace(resolvedFrom))
        {
            metadata["focus_target_resolved_from"] = resolvedFrom;
        }

        if (capabilityResult.OutputData.TryGetValue("focusMainWindowResolved", out var mainWindowResolved) &&
            !string.IsNullOrWhiteSpace(mainWindowResolved))
        {
            metadata["focus_main_window_resolved"] = mainWindowResolved;
        }
    }

    private static void EnrichProcessVerificationCapabilityMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        TargetReference target)
    {
        if (!capabilityName.Equals("ProcessVerificationCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        metadata["process_verification_capability_used"] = "true";
        metadata["verification_target_kind"] = target.Kind.ToString();
    }

    private static void EnrichProcessVerificationCapabilityResultMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        ActionExecutionResult capabilityResult)
    {
        if (!capabilityName.Equals("ProcessVerificationCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (capabilityResult.OutputData is null)
        {
            return;
        }

        if (capabilityResult.OutputData.TryGetValue("isRunning", out var isRunning) &&
            !string.IsNullOrWhiteSpace(isRunning))
        {
            metadata["process_verification_running"] = isRunning;
        }
    }

    private static void EnrichForegroundAlignmentCapabilityMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        TargetReference target)
    {
        if (!capabilityName.Equals("ForegroundAlignmentCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        metadata["foreground_alignment_capability_used"] = "true";
        metadata["alignment_target_kind"] = target.Kind.ToString();
    }

    private static void EnrichForegroundAlignmentCapabilityResultMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        ActionExecutionResult capabilityResult)
    {
        if (!capabilityName.Equals("ForegroundAlignmentCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (capabilityResult.OutputData is null)
        {
            return;
        }

        if (capabilityResult.OutputData.TryGetValue("alignmentMatchBasis", out var matchBasis) &&
            !string.IsNullOrWhiteSpace(matchBasis))
        {
            metadata["alignment_match_basis"] = matchBasis;
        }

        if (capabilityResult.OutputData.TryGetValue("alignmentResult", out var alignmentResult) &&
            !string.IsNullOrWhiteSpace(alignmentResult))
        {
            metadata["alignment_result"] = alignmentResult;
        }
    }

    private static void EnrichServiceStatusCapabilityMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        TargetReference target)
    {
        if (!capabilityName.Equals("ServiceStatusCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        metadata["service_status_capability_used"] = "true";
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            metadata["service_target_name"] = target.NormalizedValue;
        }
    }

    private static void EnrichServiceStatusCapabilityResultMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        ActionExecutionResult capabilityResult)
    {
        if (!capabilityName.Equals("ServiceStatusCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (capabilityResult.OutputData is null)
        {
            return;
        }

        if (capabilityResult.OutputData.TryGetValue("serviceName", out var serviceName) &&
            !string.IsNullOrWhiteSpace(serviceName))
        {
            metadata["service_target_name"] = serviceName;
        }

        if (capabilityResult.OutputData.TryGetValue("serviceExists", out var serviceExists) &&
            !string.IsNullOrWhiteSpace(serviceExists))
        {
            metadata["service_exists"] = serviceExists;
        }

        if (capabilityResult.OutputData.TryGetValue("serviceRunning", out var serviceRunning) &&
            !string.IsNullOrWhiteSpace(serviceRunning))
        {
            metadata["service_running"] = serviceRunning;
        }
    }

    private static void EnrichServiceControlCapabilityMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        AgentAction action,
        TargetReference target)
    {
        if (!capabilityName.Equals("ServiceControlCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        metadata["service_control_capability_used"] = "true";
        metadata["service_control_action"] = action.ActionName;
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            metadata["service_target_name"] = target.NormalizedValue;
        }
    }

    private static void EnrichServiceControlCapabilityResultMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        ActionExecutionResult capabilityResult)
    {
        if (!capabilityName.Equals("ServiceControlCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (capabilityResult.OutputData is null)
        {
            return;
        }

        if (capabilityResult.OutputData.TryGetValue("serviceName", out var serviceName) &&
            !string.IsNullOrWhiteSpace(serviceName))
        {
            metadata["service_target_name"] = serviceName;
        }

        if (capabilityResult.OutputData.TryGetValue("serviceExists", out var serviceExists) &&
            !string.IsNullOrWhiteSpace(serviceExists))
        {
            metadata["service_exists"] = serviceExists;
        }

        if (capabilityResult.OutputData.TryGetValue("serviceRunningBefore", out var serviceRunningBefore) &&
            !string.IsNullOrWhiteSpace(serviceRunningBefore))
        {
            metadata["service_running_before"] = serviceRunningBefore;
        }

        if (capabilityResult.OutputData.TryGetValue("serviceRunningAfter", out var serviceRunningAfter) &&
            !string.IsNullOrWhiteSpace(serviceRunningAfter))
        {
            metadata["service_running_after"] = serviceRunningAfter;
        }

        if (capabilityResult.OutputData.TryGetValue("serviceControlAction", out var serviceControlAction) &&
            !string.IsNullOrWhiteSpace(serviceControlAction))
        {
            metadata["service_control_action"] = serviceControlAction;
        }
    }

    private static void EnrichFileVerificationCapabilityMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        TargetReference target)
    {
        if (!capabilityName.Equals("FileVerificationCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        metadata["file_verification_capability_used"] = "true";
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            metadata["file_target_path"] = target.NormalizedValue;
        }
    }

    private static void EnrichFileVerificationCapabilityResultMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        ActionExecutionResult capabilityResult)
    {
        if (!capabilityName.Equals("FileVerificationCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (capabilityResult.OutputData is null)
        {
            return;
        }

        if (capabilityResult.OutputData.TryGetValue("filePath", out var filePath) &&
            !string.IsNullOrWhiteSpace(filePath))
        {
            metadata["file_target_path"] = filePath;
        }

        if (capabilityResult.OutputData.TryGetValue("fileExists", out var fileExists) &&
            !string.IsNullOrWhiteSpace(fileExists))
        {
            metadata["file_exists"] = fileExists;
        }
    }

    private static void EnrichFileOpenCapabilityMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        TargetReference target)
    {
        if (!capabilityName.Equals("FileOpenCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        metadata["file_open_capability_used"] = "true";
        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            metadata["file_target_path"] = target.NormalizedValue;
        }
    }

    private static void EnrichFileOpenCapabilityResultMetadata(
        IDictionary<string, string> metadata,
        string capabilityName,
        ActionExecutionResult capabilityResult)
    {
        if (!capabilityName.Equals("FileOpenCapability", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (capabilityResult.OutputData is null)
        {
            return;
        }

        if (capabilityResult.OutputData.TryGetValue("filePath", out var filePath) &&
            !string.IsNullOrWhiteSpace(filePath))
        {
            metadata["file_target_path"] = filePath;
        }

        if (capabilityResult.OutputData.TryGetValue("fileExists", out var fileExists) &&
            !string.IsNullOrWhiteSpace(fileExists))
        {
            metadata["file_exists"] = fileExists;
        }

        if (capabilityResult.OutputData.TryGetValue("fileOpened", out var fileOpened) &&
            !string.IsNullOrWhiteSpace(fileOpened))
        {
            metadata["file_opened"] = fileOpened;
        }
    }

    private static IDictionary<string, string> CreatePreDecisionMetadata(
        DecisionInputBundle decisionInputBundle,
        DecisionSummary decisionSummary,
        AiDecisionInput aiDecisionInput)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        EnrichMetadataWithDecisionInput(metadata, decisionInputBundle);
        EnrichMetadataWithDecisionSummary(metadata, decisionSummary);
        EnrichMetadataWithAiDecisionInput(metadata, aiDecisionInput);
        return metadata;
    }

    private static CommandRequest BuildAiDecisionRequest(
        CommandRequest request,
        AiDecisionInput aiDecisionInput,
        SafetyDisposition? safetyDisposition = null,
        SafetyRiskLevel? safetyRiskLevel = null,
        bool? approvalPath = null,
        bool? snapshotUsed = null,
        bool? snapshotFallback = null,
        AgentExecutionContext? executionContext = null,
        IReadOnlyDictionary<string, string>? additionalFlags = null)
    {
        var modelObservationPackage = ModelFacingObservationPackageBuilder.Build(
            request,
            aiDecisionInput,
            safetyDisposition,
            safetyRiskLevel,
            approvalPath,
            snapshotUsed,
            snapshotFallback,
            executionContext,
            additionalFlags);

        return new CommandRequest
        {
            CorrelationId = request.CorrelationId,
            UserInput = request.UserInput,
            RequestedAtUtc = request.RequestedAtUtc,
            AiDecisionInput = aiDecisionInput,
            ModelObservationPackage = modelObservationPackage,
            PrimaryTargetGrounding = request.PrimaryTargetGrounding
        };
    }

    private static DecisionInputBundle BuildRequestOnlyDecisionInputBundle(
        CommandRequest request,
        DecisionInputSource source)
    {
        if (!Guid.TryParse(request.CorrelationId, out var correlationId))
        {
            correlationId = Guid.NewGuid();
        }

        var context = new AgentExecutionContext
        {
            CorrelationId = correlationId,
            RawInput = request.UserInput,
            NormalizedInput = request.UserInput.Trim().ToLowerInvariant(),
            DetectedIntent = CommandIntentKind.Unknown,
            CreatedAtUtc = request.RequestedAtUtc,
            SessionId = null,
            Observation = null,
            RichObservation = null,
            ContextAdapter = null,
            PrimaryTargetGrounding = null,
            RuntimeState = null,
            ResolvedTargets = [],
            Metadata = null
        };

        return DecisionInputBundleBuilder.Build(context, source);
    }

    private static void EnrichCapabilityMetadataWithAdapterContext(
        IDictionary<string, string> metadata,
        AgentExecutionContext context,
        TargetReference target)
    {
        metadata["target_resolution_reason"] = target.ResolutionReasonKind.ToString();
        if (!string.IsNullOrWhiteSpace(target.ResolutionSourceText))
        {
            metadata["target_resolution_source_text"] = target.ResolutionSourceText;
        }

        var adapterContext = context.ContextAdapter;
        if (adapterContext is null)
        {
            metadata["adapterContextUsed"] = "false";
            return;
        }

        metadata["adapterContextUsed"] = "true";
        metadata["adapterContext"] = adapterContext.AdapterName;
        metadata["adapterContextAligned"] = IsAdapterContextAlignedWithTarget(adapterContext, target) ? "true" : "false";
    }

    private static void EnrichObservationRoutingPolicyMetadata(
        IDictionary<string, string> metadata,
        string routingSource)
    {
        if (routingSource.Contains("observation-hard", StringComparison.OrdinalIgnoreCase))
        {
            metadata["observation_policy_mode"] = "hard_override";
            metadata["observation_conflict_detected"] = "true";
            metadata["observation_override_applied"] = "true";
            metadata["observation_routing_score_basis"] = "window_handle_plus_process_conflict";
            return;
        }

        if (routingSource.Contains("observation-soft", StringComparison.OrdinalIgnoreCase))
        {
            metadata["observation_policy_mode"] = "soft_guidance";
            metadata["observation_conflict_detected"] = "false";
            metadata["observation_override_applied"] = "false";
            metadata["observation_routing_score_basis"] = "observation_weighted_target_scoring";
        }
    }

    private static bool IsAdapterContextAlignedWithTarget(ContextAdapterContext adapterContext, TargetReference target)
    {
        if (target.Kind is TargetKind.Application or TargetKind.Process)
        {
            return !string.IsNullOrWhiteSpace(adapterContext.ProcessName) &&
                   adapterContext.ProcessName.Equals(target.NormalizedValue, StringComparison.OrdinalIgnoreCase);
        }

        if (target.Kind == TargetKind.Window)
        {
            return adapterContext.WindowHandle.HasValue &&
                   adapterContext.WindowHandle.Value.ToString().Equals(target.NormalizedValue, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static IReadOnlyList<TargetReference> ApplyAdapterContextReasonBridge(
        IReadOnlyList<TargetReference> targets,
        ContextAdapterContext? adapterContext)
    {
        if (adapterContext is null || targets.Count == 0)
        {
            return targets;
        }

        List<TargetReference>? bridgedTargets = null;

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var isAligned = IsAdapterContextAlignedWithTarget(adapterContext, target);
            if (!TargetResolutionReasonPolicy.TryPromoteObservationToAdapterContext(
                    target,
                    adapterContext.AdapterName,
                    isAligned,
                    out var adapterSourceText))
            {
                continue;
            }

            bridgedTargets ??= new List<TargetReference>(targets);
            bridgedTargets[i] = CloneWithResolutionReason(
                target,
                TargetResolutionReasonKind.AdapterContext,
                adapterSourceText);
        }

        return bridgedTargets ?? targets;
    }

    private static TargetReference CloneWithResolutionReason(
        TargetReference target,
        TargetResolutionReasonKind reasonKind,
        string? sourceText)
    {
        return new TargetReference
        {
            Kind = target.Kind,
            OriginalText = target.OriginalText,
            NormalizedValue = target.NormalizedValue,
            DisplayName = target.DisplayName,
            Confidence = target.Confidence,
            ResolutionReasonKind = reasonKind,
            ResolutionSourceText = sourceText,
            Metadata = target.Metadata
        };
    }

    private static AgentExecutionContext EnsureInvocationTargetInContext(
        AgentExecutionContext context,
        TargetReference target)
    {
        if (string.IsNullOrWhiteSpace(target.NormalizedValue) &&
            string.IsNullOrWhiteSpace(target.DisplayName) &&
            string.IsNullOrWhiteSpace(target.OriginalText))
        {
            return context;
        }

        var existingTargets = context.ResolvedTargets;
        var hasEquivalentTarget = existingTargets.Any(existing =>
        {
            var sameIdentity =
                (!string.IsNullOrWhiteSpace(existing.NormalizedValue) &&
                 !string.IsNullOrWhiteSpace(target.NormalizedValue) &&
                 string.Equals(existing.NormalizedValue, target.NormalizedValue, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(existing.DisplayName) &&
                 !string.IsNullOrWhiteSpace(target.DisplayName) &&
                 string.Equals(existing.DisplayName, target.DisplayName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(existing.OriginalText) &&
                 !string.IsNullOrWhiteSpace(target.OriginalText) &&
                 string.Equals(existing.OriginalText, target.OriginalText, StringComparison.OrdinalIgnoreCase));

            var kindCompatible =
                existing.Kind == target.Kind ||
                (existing.Kind is TargetKind.Application or TargetKind.Process &&
                 target.Kind is TargetKind.Application or TargetKind.Process) ||
                (existing.Kind is TargetKind.File or TargetKind.Path &&
                 target.Kind is TargetKind.File or TargetKind.Path);

            return sameIdentity && kindCompatible;
        });
        if (hasEquivalentTarget)
        {
            return context;
        }

        var mergedTargets = new List<TargetReference>(existingTargets.Count + 1) { target };
        mergedTargets.AddRange(existingTargets);

        return new AgentExecutionContext
        {
            CorrelationId = context.CorrelationId,
            RawInput = context.RawInput,
            NormalizedInput = context.NormalizedInput,
            DetectedIntent = context.DetectedIntent,
            CreatedAtUtc = context.CreatedAtUtc,
            SessionId = context.SessionId,
            Observation = context.Observation,
            RichObservation = context.RichObservation,
            ContextAdapter = context.ContextAdapter,
            PrimaryTargetGrounding = context.PrimaryTargetGrounding,
            RuntimeState = context.RuntimeState,
            ResolvedTargets = mergedTargets,
            Metadata = context.Metadata is null
                ? null
                : new Dictionary<string, string>(context.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task<IReadOnlyList<TargetReference>> ResolveTargetsForPendingSnapshotAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _targetResolver.ResolveTargetsAsync(request, observation: null, cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    private async Task<(
        ContextAdapterContext? ContextAdapter,
        PendingObservationSummary? ObservationSummary,
        CommandObservationSnapshot CommandObservation)> CapturePendingSnapshotContextAsync(
        CommandRequest request,
        SafetyDecision safetyDecision,
        CancellationToken cancellationToken)
    {
        ObservationSnapshot? observation = null;
        ContextAdapterContext? adapterContext = null;

        try
        {
            observation = await _observationProvider.CaptureAsync(cancellationToken);
        }
        catch
        {
            // Snapshot context capture is best-effort.
        }

        if (observation is not null && _contextAdapterResolver is not null)
        {
            try
            {
                adapterContext = _contextAdapterResolver.Resolve(observation);
            }
            catch
            {
                // Adapter capture is optional for snapshot context.
            }
        }

        var commandObservation = await CaptureCommandObservationAsync(
            new CommandObservationRequest
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                OriginalUserCommand = request.UserInput,
                BaselineObservation = observation,
                PrimaryTargetGrounding = null,
                SafetyDisposition = safetyDecision.Disposition,
                SafetyRiskLevel = safetyDecision.RiskLevel
            },
            cancellationToken);

        return (adapterContext, BuildPendingObservationSummary(observation), commandObservation);
    }

    private static PendingObservationSummary? BuildPendingObservationSummary(ObservationSnapshot? observation)
    {
        if (observation is null)
        {
            return null;
        }

        var summary = new PendingObservationSummary
        {
            ActiveProcessName = observation.ActiveProcessName,
            ActiveWindowTitle = observation.ActiveWindow?.Title,
            ActiveWindowHandle = observation.ActiveWindow?.Handle
        };

        if (string.IsNullOrWhiteSpace(summary.ActiveProcessName) &&
            string.IsNullOrWhiteSpace(summary.ActiveWindowTitle) &&
            !summary.ActiveWindowHandle.HasValue)
        {
            return null;
        }

        return summary;
    }

    private static PendingApprovalSnapshot BuildPendingApprovalSnapshot(
        CommandRequest request,
        AiDecision decision,
        IReadOnlyList<TargetReference> resolvedTargets,
        ContextAdapterContext? contextAdapter,
        PendingObservationSummary? observationSummary,
        DecisionCycleRuntimeState? runtimeState = null)
    {
        var normalizedDecision = NormalizeDecision(decision);
        var pendingDecisionContract = normalizedDecision.NextActionDecision;
        var approvedDecisionContract = BuildApprovedDecisionContract(pendingDecisionContract);
        var pendingStepApprovalKey = ResolveApprovalStepKey(pendingDecisionContract);
        var approvedStepApprovalKey = ResolveApprovalStepKey(approvedDecisionContract);
        var effectiveRuntimeState = runtimeState ?? CreatePendingApprovalRuntimeState(
            request,
            pendingDecisionContract,
            approvedDecisionContract);

        return new PendingApprovalSnapshot
        {
            CorrelationId = request.CorrelationId,
            CommandText = request.UserInput,
            Decision = normalizedDecision,
            PendingDecisionContract = pendingDecisionContract,
            ApprovedDecisionContract = approvedDecisionContract,
            ResolvedTargets = resolvedTargets,
            ContextAdapter = contextAdapter,
            ObservationSummary = observationSummary,
            RuntimeState = effectiveRuntimeState,
            PendingStepApprovalKey = pendingStepApprovalKey,
            ApprovedStepApprovalKey = approvedStepApprovalKey,
            CreatedAtUtc = request.RequestedAtUtc,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pending_contract_present"] = pendingDecisionContract is not null ? "true" : "false",
                ["approved_contract_present"] = approvedDecisionContract is not null ? "true" : "false",
                ["runtime_state_present"] = "true",
                ["runtime_state_synthesized"] = runtimeState is null ? "true" : "false",
                ["pending_step_approval_key_present"] = string.IsNullOrWhiteSpace(pendingStepApprovalKey) ? "false" : "true",
                ["approved_step_approval_key_present"] = string.IsNullOrWhiteSpace(approvedStepApprovalKey) ? "false" : "true",
                ["approval_resume_restore_ready"] = approvedDecisionContract is not null ? "true" : "false"
            }
        };
    }

    private static DecisionCycleRuntimeState CreatePendingApprovalRuntimeState(
        CommandRequest request,
        NextActionDecision? pendingDecisionContract,
        NextActionDecision? approvedDecisionContract)
    {
        var runtimeState = CreateInitialDecisionCycleRuntimeState(
            request,
            DecisionInputSource.Live,
            maxStepLimit: DefaultDecisionLoopMaxSteps,
            maxRetryLimit: DefaultDecisionRetryLimit);

        runtimeState.CurrentStepIndex = 1;
        runtimeState.CurrentDecision = pendingDecisionContract;
        runtimeState.AwaitingApproval = true;
        runtimeState.CompletionState = DecisionRuntimeCompletionState.Terminal;
        runtimeState.TerminalState = DecisionLoopTerminalState.AwaitingApproval;
        runtimeState.BlockedState = DecisionRuntimeBlockedState.Blocked;
        runtimeState.GoalStillActive = true;
        runtimeState.Outcome = DecisionRuntimeOutcome.Blocked;
        runtimeState.TerminationReason = DecisionRuntimeTerminationReason.AwaitingApproval;

        var effectiveDecision = pendingDecisionContract ?? approvedDecisionContract ?? BuildStopDecisionContract("Pending approval snapshot is missing a resumable action.");
        UpdateRuntimeSessionCurrentStep(runtimeState, effectiveDecision);

        runtimeState.LastExecutionFeedback = new DecisionExecutionFeedback
        {
            ExecutedActionSummary = "approval-requested",
            ExecutionSucceeded = false,
            ExecutionFailed = false,
            ExecutionBlocked = true,
            ExecutionMessage = BuildDecisionTextFromNextAction(effectiveDecision),
            NormalizedOutcome = DecisionExecutionOutcome.Blocked,
            ProducedResult = null,
            ProducedTarget = effectiveDecision.ExecuteAction?.Target.Reference ?? effectiveDecision.AskApproval?.ProposedAction?.Target.Reference,
            FailureCategory = DecisionFailureCategory.BlockedByApproval,
            BlockedReason = BuildDecisionTextFromNextAction(effectiveDecision),
            Verification = new DecisionVerificationSummary
            {
                Status = null,
                Summary = "Execution paused for approval.",
                Reason = "Execution paused for approval.",
                TargetReached = null
            },
            Approval = new DecisionApprovalSummary
            {
                ApprovalRequired = true,
                ApprovalPath = false,
                Approved = null,
                Reason = BuildDecisionTextFromNextAction(effectiveDecision)
            }
        };

        UpdateRuntimeVerificationState(
            runtimeState,
            runtimeState.LastExecutionFeedback.Verification,
            runtimeState.LastExecutionFeedback.ProducedTarget);

        AppendDecisionCycleStep(
            runtimeState,
            decisionKind: effectiveDecision.Kind,
            actionType: effectiveDecision.ExecuteAction?.ActionType ?? effectiveDecision.AskApproval?.ProposedAction?.ActionType,
            targetSummary: effectiveDecision.ExecuteAction?.Target.Reference ?? effectiveDecision.AskApproval?.ProposedAction?.Target.Reference,
            outcome: DecisionExecutionOutcome.Blocked,
            transition: DecisionLoopTransition.AwaitApproval,
            terminalState: runtimeState.TerminalState,
            reason: runtimeState.LastExecutionFeedback.ExecutionMessage);

        return runtimeState;
    }

    private static bool TryCreateApprovedExecutionStateFromSnapshot(
        CommandRequest request,
        PendingApprovalSnapshot? snapshot,
        SafetyRiskLevel riskLevel,
        out AgentExecutionContext executionContext,
        out AiDecision decision,
        out bool targetReasonNormalized,
        out bool adapterContextRestored,
        out bool observationSummaryRestored,
        out string? failureReason)
    {
        executionContext = default!;
        decision = default!;
        targetReasonNormalized = false;
        adapterContextRestored = false;
        observationSummaryRestored = false;
        failureReason = null;

        if (snapshot is null)
        {
            failureReason = "missing_snapshot";
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.CorrelationId) ||
            !snapshot.CorrelationId.Equals(request.CorrelationId, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = "correlation_mismatch";
            return false;
        }

        if (!snapshot.CommandText.Equals(request.UserInput, StringComparison.Ordinal))
        {
            failureReason = "command_mismatch";
            return false;
        }

        if (snapshot.RuntimeState is null)
        {
            failureReason = "missing_runtime_state";
            return false;
        }

        decision = BuildDecisionForApprovedSnapshot(snapshot);
        if (decision.NextActionDecision?.Kind != DecisionKind.ExecuteAction ||
            decision.NextActionDecision.ExecuteAction is null)
        {
            failureReason = "invalid_contract";
            return false;
        }

        var restoredAdapterContext = snapshot.ContextAdapter;
        var restoredObservation = TryRehydrateObservationFromSummary(snapshot.ObservationSummary, snapshot.CreatedAtUtc);
        var restoredCommandObservation = CreateFallbackCommandObservationSnapshot(
            new CommandObservationRequest
            {
                TimestampUtc = snapshot.CreatedAtUtc,
                OriginalUserCommand = request.UserInput,
                BaselineObservation = restoredObservation,
                PrimaryTargetGrounding = null,
                SafetyDisposition = SafetyDisposition.RequiresApproval,
                SafetyRiskLevel = riskLevel
            },
            restoredObservation is null ? ObservationCollectionStatus.Partial : ObservationCollectionStatus.Success,
            "Restored from approval snapshot context.");

        var normalizedTargets = NormalizeSnapshotTargets(snapshot.ResolvedTargets ?? [], out targetReasonNormalized);
        normalizedTargets = ApplyAdapterContextReasonBridge(normalizedTargets, restoredAdapterContext);

        if (!Guid.TryParse(request.CorrelationId, out var correlationId))
        {
            correlationId = Guid.NewGuid();
        }

        adapterContextRestored = restoredAdapterContext is not null;
        observationSummaryRestored = restoredObservation is not null;

        executionContext = new AgentExecutionContext
        {
            CorrelationId = correlationId,
            RawInput = request.UserInput,
            NormalizedInput = request.UserInput.Trim().ToLowerInvariant(),
            DetectedIntent = DetectIntentKind(
                request.UserInput.Trim().ToLowerInvariant(),
                normalizedTargets,
                grounding: null,
                decision: decision),
            CreatedAtUtc = snapshot.CreatedAtUtc,
            SessionId = null,
            Observation = restoredObservation,
            RichObservation = restoredCommandObservation,
            ContextAdapter = restoredAdapterContext,
            PrimaryTargetGrounding = null,
            RuntimeState = null,
            ResolvedTargets = normalizedTargets,
            Metadata = BuildExecutionContextMetadata(restoredAdapterContext, null)
        };

        return true;
    }

    private static string? ResolveApprovedStepApprovalKey(PendingApprovalSnapshot? snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot?.ApprovedStepApprovalKey))
        {
            return snapshot.ApprovedStepApprovalKey;
        }

        return ResolveApprovalStepKey(snapshot?.ApprovedDecisionContract) ??
               ResolveApprovalStepKey(snapshot?.PendingDecisionContract) ??
               ResolveApprovalStepKey(snapshot?.Decision.NextActionDecision);
    }

    private static string? ResolveApprovalStepKey(NextActionDecision? decisionContract)
    {
        if (decisionContract is null)
        {
            return null;
        }

        if (decisionContract.Kind == DecisionKind.ExecuteAction)
        {
            return StepApprovalIdentityBuilder.BuildForExecuteAction(decisionContract.ExecuteAction);
        }

        if (decisionContract.Kind == DecisionKind.AskApproval)
        {
            return StepApprovalIdentityBuilder.BuildForExecuteAction(decisionContract.AskApproval?.ProposedAction);
        }

        return null;
    }

    private static bool TryBuildRuntimeApprovalDecision(
        AgentAction? agentAction,
        string message,
        out AiDecision decision)
    {
        decision = new AiDecision();

        if (!TryBuildExecuteActionFromAgentAction(agentAction, out var executeAction))
        {
            return false;
        }

        var decisionText = string.IsNullOrWhiteSpace(message)
            ? "Approval granted. Executing runtime-selected step."
            : message;

        decision = NormalizeDecision(new AiDecision
        {
            SelectedToolName = MapActionTypeToToolName(executeAction.ActionType),
            ToolArgument = ExtractToolArgumentFromExecuteAction(executeAction),
            DecisionText = decisionText,
            NextActionDecision = new NextActionDecision
            {
                Kind = DecisionKind.ExecuteAction,
                Message = decisionText,
                ExecuteAction = executeAction
            },
            RoutedCapabilityName = agentAction?.CapabilityName,
            RoutedActionName = agentAction?.ActionName
        });

        return true;
    }

    private static bool TryBuildExecuteActionFromAgentAction(
        AgentAction? agentAction,
        out ExecuteActionPayload executeAction)
    {
        executeAction = new ExecuteActionPayload();

        if (agentAction is null || agentAction.Target is null)
        {
            return false;
        }

        if (!TryMapAgentActionToActionType(agentAction, out var actionType))
        {
            return false;
        }

        executeAction = new ExecuteActionPayload
        {
            ActionType = actionType,
            Target = BuildActionTargetFromTargetReference(agentAction.Target),
            Parameters = new ActionParameters
            {
                Values = agentAction.Parameters is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(agentAction.Parameters, StringComparer.OrdinalIgnoreCase)
            },
            CapabilityHint = agentAction.CapabilityName
        };

        return true;
    }

    private static bool TryMapAgentActionToActionType(AgentAction agentAction, out ActionType actionType)
    {
        if (agentAction.ActionName.Equals("OpenApplication", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Launch;
            return true;
        }

        if (agentAction.ActionName.Equals("FocusWindow", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Focus;
            return true;
        }

        if (agentAction.ActionName.Equals("OpenExistingFile", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.OpenFile;
            return true;
        }

        if (agentAction.ActionName.StartsWith("Verify", StringComparison.OrdinalIgnoreCase))
        {
            actionType = ActionType.Verify;
            return true;
        }

        actionType = ActionType.Unknown;
        return false;
    }

    private static ActionTarget BuildActionTargetFromTargetReference(TargetReference target)
    {
        return new ActionTarget
        {
            Kind = MapTargetKindToActionTargetKind(target.Kind),
            Reference = !string.IsNullOrWhiteSpace(target.NormalizedValue)
                ? target.NormalizedValue
                : !string.IsNullOrWhiteSpace(target.DisplayName)
                    ? target.DisplayName
                    : target.OriginalText,
            DisplayName = target.DisplayName,
            Metadata = target.Metadata is null
                ? null
                : new Dictionary<string, string>(target.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static ActionTargetKind MapTargetKindToActionTargetKind(TargetKind targetKind)
    {
        return targetKind switch
        {
            TargetKind.Application => ActionTargetKind.Application,
            TargetKind.Process => ActionTargetKind.Process,
            TargetKind.Window => ActionTargetKind.Window,
            TargetKind.File => ActionTargetKind.File,
            TargetKind.Path => ActionTargetKind.Path,
            TargetKind.Directory => ActionTargetKind.Path,
            TargetKind.Url => ActionTargetKind.Url,
            TargetKind.Service => ActionTargetKind.Service,
            TargetKind.UiElement => ActionTargetKind.Element,
            _ => ActionTargetKind.Generic
        };
    }

    private static AiDecision BuildDecisionForApprovedSnapshot(PendingApprovalSnapshot snapshot)
    {
        var approvedDecisionContract = ResolveApprovedDecisionContract(snapshot);
        if (approvedDecisionContract is null)
        {
            return NormalizeDecision(snapshot.Decision);
        }

        var approvedDecision = new AiDecision
        {
            SelectedToolName = snapshot.Decision.SelectedToolName,
            ToolArgument = snapshot.Decision.ToolArgument,
            DecisionText = BuildDecisionTextFromNextAction(approvedDecisionContract),
            NextActionDecision = approvedDecisionContract,
            RoutedCapabilityName = snapshot.Decision.RoutedCapabilityName,
            RoutedActionName = snapshot.Decision.RoutedActionName,
            IsLegacyFallback = snapshot.Decision.IsLegacyFallback
        };

        return NormalizeDecision(approvedDecision);
    }

    private static NextActionDecision? ResolveApprovedDecisionContract(PendingApprovalSnapshot snapshot)
    {
        if (snapshot.ApprovedDecisionContract is not null)
        {
            return snapshot.ApprovedDecisionContract;
        }

        var pendingDecisionContract = snapshot.PendingDecisionContract ?? snapshot.Decision.NextActionDecision;
        return BuildApprovedDecisionContract(pendingDecisionContract);
    }

    private static NextActionDecision? BuildApprovedDecisionContract(NextActionDecision? pendingDecisionContract)
    {
        if (pendingDecisionContract is null)
        {
            return null;
        }

        if (pendingDecisionContract.Kind == DecisionKind.ExecuteAction)
        {
            if (pendingDecisionContract.ExecuteAction is null)
            {
                return BuildStopDecisionContract("Approved snapshot has an invalid execute-action payload.");
            }

            return pendingDecisionContract;
        }

        if (pendingDecisionContract.Kind == DecisionKind.AskApproval)
        {
            var proposedAction = pendingDecisionContract.AskApproval?.ProposedAction;
            if (proposedAction is null)
            {
                return BuildStopDecisionContract("Approved snapshot did not include a proposed action to execute.");
            }

            return new NextActionDecision
            {
                Kind = DecisionKind.ExecuteAction,
                ExecuteAction = proposedAction,
                Message = string.IsNullOrWhiteSpace(pendingDecisionContract.Message)
                    ? "Approval granted. Executing proposed action."
                    : pendingDecisionContract.Message,
                Rationale = pendingDecisionContract.Rationale,
                Confidence = pendingDecisionContract.Confidence,
                Metadata = pendingDecisionContract.Metadata
            };
        }

        if (pendingDecisionContract.Kind == DecisionKind.Stop)
        {
            if (pendingDecisionContract.Stop is not null)
            {
                return pendingDecisionContract;
            }

            var stopReason = string.IsNullOrWhiteSpace(pendingDecisionContract.StopReason)
                ? "Execution stopped by model decision."
                : pendingDecisionContract.StopReason;
            return BuildStopDecisionContract(stopReason);
        }

        if (pendingDecisionContract.Kind == DecisionKind.Retry)
        {
            var retryReason = pendingDecisionContract.RetryReason ?? pendingDecisionContract.Retry?.RetryReason;
            return BuildStopDecisionContract(string.IsNullOrWhiteSpace(retryReason)
                ? "Execution blocked: model requested retry before approval."
                : retryReason);
        }

        if (pendingDecisionContract.Kind == DecisionKind.AskObserve)
        {
            var observeReason = pendingDecisionContract.AskObserve?.ObservationRequest;
            return BuildStopDecisionContract(string.IsNullOrWhiteSpace(observeReason)
                ? "Execution blocked: model requested additional observation."
                : observeReason);
        }

        return BuildStopDecisionContract("Execution blocked: unsupported pending decision kind.");
    }

    private static NextActionDecision BuildStopDecisionContract(string reason)
    {
        return new NextActionDecision
        {
            Kind = DecisionKind.Stop,
            Message = reason,
            StopReason = reason,
            Stop = new StopPayload
            {
                Disposition = StopDisposition.Blocked,
                StopReason = reason
            }
        };
    }

    private static ObservationSnapshot? TryRehydrateObservationFromSummary(
        PendingObservationSummary? summary,
        DateTimeOffset capturedAtUtc)
    {
        if (summary is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(summary.ActiveProcessName) &&
            string.IsNullOrWhiteSpace(summary.ActiveWindowTitle) &&
            !summary.ActiveWindowHandle.HasValue)
        {
            return null;
        }

        var hasWindowData = !string.IsNullOrWhiteSpace(summary.ActiveWindowTitle) || summary.ActiveWindowHandle.HasValue;

        return new ObservationSnapshot
        {
            ActiveWindow = hasWindowData
                ? new WindowContext
                {
                    Title = summary.ActiveWindowTitle,
                    ProcessName = summary.ActiveProcessName,
                    Handle = summary.ActiveWindowHandle,
                    IsForeground = true
                }
                : null,
            ActiveProcessName = summary.ActiveProcessName,
            ClipboardTextPreview = null,
            HasSelection = false,
            SelectionTextPreview = null,
            DesktopStateSummary = "Restored from approval snapshot context.",
            CapturedAtUtc = capturedAtUtc
        };
    }

    private static IReadOnlyList<TargetReference> NormalizeSnapshotTargets(
        IReadOnlyList<TargetReference> targets,
        out bool normalizedAny)
    {
        normalizedAny = false;

        if (targets.Count == 0)
        {
            return targets;
        }

        List<TargetReference>? normalizedTargets = null;

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!TargetResolutionReasonPolicy.TryNormalizeUnknownReason(target, out var reasonKind, out var sourceText))
            {
                continue;
            }

            normalizedTargets ??= new List<TargetReference>(targets);
            normalizedTargets[i] = CloneWithResolutionReason(target, reasonKind, sourceText);
            normalizedAny = true;
        }

        return normalizedTargets ?? targets;
    }

    private static bool HasOnlyUnknownTargetReasons(IReadOnlyList<TargetReference> targets)
    {
        return targets.Count > 0 &&
               targets.All(target => target.ResolutionReasonKind == TargetResolutionReasonKind.Unknown);
    }

    private async Task LogEventAsync(
        CommandRequest request,
        string eventType,
        string safetyDisposition,
        string riskLevel,
        string selectedTool,
        string executionMode,
        string outcome,
        string message,
        string approvalDecision,
        CancellationToken cancellationToken,
        AgentExecutionContext? executionContext = null,
        IDictionary<string, string>? extraMetadata = null)
    {
        var metadata = BuildAuditMetadata(executionContext, extraMetadata) ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var verificationOutcomeCacheKey = BuildVerificationOutcomeCacheKey(request.CorrelationId, executionContext?.CorrelationId);

        await _auditLogger.LogAsync(new AuditEvent
        {
            CorrelationId = request.CorrelationId,
            EventType = eventType,
            CommandText = request.UserInput,
            SafetyDisposition = safetyDisposition,
            RiskLevel = riskLevel,
            SelectedTool = selectedTool,
            ExecutionMode = executionMode,
            Outcome = outcome,
            Message = message,
            ApprovalDecision = approvalDecision,
            Metadata = metadata
        }, cancellationToken);

        if (ShouldEmitStepExecutedEvent(eventType, executionMode))
        {
            var stepExecutionMetadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["step_executed"] = "true",
                ["execution_outcome"] = outcome,
                ["step_execution_message"] = message,
                ["step_executed_source_event"] = eventType
            };

            if (!stepExecutionMetadata.ContainsKey("runtimeId"))
            {
                stepExecutionMetadata["runtimeId"] = request.CorrelationId;
            }

            if (!stepExecutionMetadata.ContainsKey("runtimeStepIndex") &&
                stepExecutionMetadata.TryGetValue("submitted_step_index", out var submittedStepIndex) &&
                !string.IsNullOrWhiteSpace(submittedStepIndex))
            {
                stepExecutionMetadata["runtimeStepIndex"] = submittedStepIndex;
            }

            await _auditLogger.LogAsync(new AuditEvent
            {
                CorrelationId = request.CorrelationId,
                EventType = "StepExecuted",
                CommandText = request.UserInput,
                SafetyDisposition = safetyDisposition,
                RiskLevel = riskLevel,
                SelectedTool = selectedTool,
                ExecutionMode = executionMode,
                Outcome = outcome,
                Message = message,
                ApprovalDecision = approvalDecision,
                Metadata = stepExecutionMetadata
            }, cancellationToken);
        }

        var isCommandCompletedEvent = string.Equals(eventType, "CommandCompleted", StringComparison.OrdinalIgnoreCase);
        if (!ShouldEmitRuntimeTerminatedEvent(eventType))
        {
            if (isCommandCompletedEvent)
            {
                lock (_verificationOutcomeByCorrelationId)
                {
                    _verificationOutcomeByCorrelationId.Remove(verificationOutcomeCacheKey);
                }

                lock (_submittedStepEventByCorrelationId)
                {
                    _submittedStepEventByCorrelationId.Remove(verificationOutcomeCacheKey);
                }
            }

            return;
        }

        lock (_verificationOutcomeByCorrelationId)
        {
            if (!metadata.ContainsKey("verification_result_outcome") &&
                _verificationOutcomeByCorrelationId.TryGetValue(verificationOutcomeCacheKey, out var verificationOutcome) &&
                !string.IsNullOrWhiteSpace(verificationOutcome))
            {
                metadata["verification_result_outcome"] = verificationOutcome;
            }

            _verificationOutcomeByCorrelationId.Remove(verificationOutcomeCacheKey);
        }

        var runtimeTerminationMetadata = BuildRuntimeTerminationMetadata(
            metadata,
            outcome,
            approvalDecision,
            request.CorrelationId);

        await _auditLogger.LogAsync(new AuditEvent
        {
            CorrelationId = request.CorrelationId,
            EventType = "RuntimeTerminated",
            CommandText = request.UserInput,
            SafetyDisposition = safetyDisposition,
            RiskLevel = riskLevel,
            SelectedTool = selectedTool,
            ExecutionMode = executionMode,
            Outcome = outcome,
            Message = message,
            ApprovalDecision = approvalDecision,
            Metadata = runtimeTerminationMetadata
        }, cancellationToken);

        if (isCommandCompletedEvent)
        {
            lock (_verificationOutcomeByCorrelationId)
            {
                _verificationOutcomeByCorrelationId.Remove(verificationOutcomeCacheKey);
            }

            lock (_submittedStepEventByCorrelationId)
            {
                _submittedStepEventByCorrelationId.Remove(verificationOutcomeCacheKey);
            }
        }
    }

    private static string BuildSubmittedStepDedupKey(
        DecisionCycleRuntimeState runtimeState,
        AiDecision? decision)
    {
        var nextAction = decision?.NextActionDecision ?? runtimeState.CurrentDecision;
        var action = nextAction?.ExecuteAction ?? nextAction?.AskApproval?.ProposedAction ?? nextAction?.Retry?.Action;
        var decisionKind = nextAction?.Kind ?? DecisionKind.Stop;
        var actionType = action is null ? "none" : action.ActionType.ToString();
        var target = action?.Target.Reference ?? "none";
        return $"{runtimeState.CurrentStepIndex}|{decisionKind}|{actionType}|{target}";
    }

    private static string BuildVerificationOutcomeCacheKey(string requestCorrelationId, Guid? runtimeCorrelationId)
    {
        return runtimeCorrelationId.HasValue
            ? runtimeCorrelationId.Value.ToString("N")
            : BuildCacheKeyFromRequestCorrelationId(requestCorrelationId);
    }

    private static string BuildCacheKeyFromRequestCorrelationId(string requestCorrelationId)
    {
        if (Guid.TryParse(requestCorrelationId, out var parsedCorrelationId))
        {
            return parsedCorrelationId.ToString("N");
        }

        return requestCorrelationId;
    }

    private static bool ShouldEmitStepExecutedEvent(string eventType, string executionMode)
    {
        if (string.Equals(executionMode, "not-executed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(eventType, "ToolExecutionCompleted", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "CapabilityExecutionCompleted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldEmitRuntimeTerminatedEvent(string eventType)
    {
        return string.Equals(eventType, "CommandCompleted", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildRuntimeTerminationMetadata(
        IDictionary<string, string> metadata,
        string finalCommandStatus,
        string approvalDecision,
        string correlationId)
    {
        var runtimeMetadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);

        var terminalState = NormalizeTerminalState(runtimeMetadata, finalCommandStatus, approvalDecision);
        var terminalReasonCode = NormalizeTerminalReasonCode(runtimeMetadata, finalCommandStatus, approvalDecision);
        var terminalSource = NormalizeTerminalSource(runtimeMetadata, terminalReasonCode);

        runtimeMetadata["terminal_state"] = terminalState;
        runtimeMetadata["terminal_reason_code"] = terminalReasonCode;
        runtimeMetadata["terminal_source"] = terminalSource;
        runtimeMetadata["final_command_status"] = finalCommandStatus;

        if (!runtimeMetadata.ContainsKey("runtimeId"))
        {
            runtimeMetadata["runtimeId"] = correlationId;
        }

        if (!runtimeMetadata.ContainsKey("runtimeStepIndex") &&
            runtimeMetadata.TryGetValue("submitted_step_index", out var submittedStepIndex) &&
            !string.IsNullOrWhiteSpace(submittedStepIndex))
        {
            runtimeMetadata["runtimeStepIndex"] = submittedStepIndex;
        }

        return runtimeMetadata;
    }

    private static string NormalizeTerminalState(
        IDictionary<string, string> metadata,
        string finalCommandStatus,
        string approvalDecision)
    {
        if (metadata.TryGetValue("haltReason", out var haltReason) &&
            string.Equals(haltReason, "approval_rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "rejected";
        }

        if (string.Equals(approvalDecision, "Rejected", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalCommandStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "rejected";
        }

        if (metadata.TryGetValue("runtimeTerminalState", out var runtimeTerminalState) &&
            !string.IsNullOrWhiteSpace(runtimeTerminalState))
        {
            return runtimeTerminalState switch
            {
                "Completed" => "completed",
                "Aborted" => "aborted",
                _ => "blocked"
            };
        }

        return finalCommandStatus switch
        {
            "Accepted" => "completed",
            "Rejected" => "rejected",
            "Denied" => "blocked",
            _ => "blocked"
        };
    }

    private static string NormalizeTerminalReasonCode(
        IDictionary<string, string> metadata,
        string finalCommandStatus,
        string approvalDecision)
    {
        if (metadata.TryGetValue("haltReason", out var haltReason) &&
            !string.IsNullOrWhiteSpace(haltReason))
        {
            return haltReason switch
            {
                "approval_rejected" => "approval_rejected",
                "approval_resume_failed" => "approval_resume_failed",
                _ => haltReason
            };
        }

        if (string.Equals(approvalDecision, "Rejected", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalCommandStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "approval_rejected";
        }

        if (metadata.TryGetValue("verification_result_outcome", out var verificationOutcome) &&
            string.Equals(verificationOutcome, "VerifiedFailure", StringComparison.OrdinalIgnoreCase))
        {
            return "verification_failed_stop";
        }

        if (metadata.TryGetValue("runtimeTerminationReason", out var runtimeTerminationReason) &&
            !string.IsNullOrWhiteSpace(runtimeTerminationReason))
        {
            if (runtimeTerminationReason == "NonRetryableFailure" &&
                string.Equals(finalCommandStatus, "Denied", StringComparison.OrdinalIgnoreCase))
            {
                return "verification_failed_stop";
            }

            return runtimeTerminationReason switch
            {
                "GoalCompleted" => "goal_completed",
                "ApprovalRejected" => "approval_rejected",
                "RetryLimitReached" => "retry_limit",
                "MaxStepLimitReached" => "max_step",
                "FatalException" => "fatal",
                "SafetyDenied" => "safety_denied",
                "AwaitingApproval" => "awaiting_approval",
                "HardStop" => "hard_stop",
                "NonRetryableFailure" => "blocked_non_retryable",
                _ => runtimeTerminationReason
            };
        }

        return finalCommandStatus switch
        {
            "Accepted" => "goal_completed",
            "Rejected" => "approval_rejected",
            "Denied" => "verification_failed_stop",
            _ => "blocked_non_retryable"
        };
    }

    private static string NormalizeTerminalSource(IDictionary<string, string> metadata, string terminalReasonCode)
    {
        return terminalReasonCode switch
        {
            "approval_rejected" => "approval_reject",
            "approval_resume_failed" => "approval_resume_failed",
            "verification_failed_stop" => "post_execution_transition",
            "safety_denied" => "safety_gate",
            "fatal" => "exception",
            "max_step" => "max_step",
            _ => metadata.ContainsKey("next_action_stop_disposition")
                ? "model_stop"
                : "post_execution_transition"
        };
    }

    private async Task<AgentExecutionContext> BuildExecutionContextAsync(
        CommandRequest request,
        SafetyDisposition safetyDisposition,
        SafetyRiskLevel riskLevel,
        CancellationToken cancellationToken)
    {
        ObservationSnapshot? observation = null;
        IReadOnlyList<TargetReference> targets = [];
        ContextAdapterContext? adapterContext = null;
        TargetGroundingResult? primaryTargetGrounding = null;

        try
        {
            observation = await _observationProvider.CaptureAsync(cancellationToken);
        }
        catch
        {
            // Observation is optional at this foundation stage.
        }

        try
        {
            targets = await _targetResolver.ResolveTargetsAsync(request, observation, cancellationToken);
        }
        catch
        {
            // Target resolution is best-effort at this foundation stage.
        }

        if (observation is not null && _contextAdapterResolver is not null)
        {
            try
            {
                adapterContext = _contextAdapterResolver.Resolve(observation);
            }
            catch
            {
                // Adapter resolution is optional at this foundation stage.
            }
        }

        targets = ApplyAdapterContextReasonBridge(targets, adapterContext);

        if (_targetGrounder is not null)
        {
            try
            {
                primaryTargetGrounding = await _targetGrounder.GroundAsync(request, targets, cancellationToken);
            }
            catch
            {
                // Target grounding is best-effort at this foundation stage.
            }
        }

        if (!Guid.TryParse(request.CorrelationId, out var correlationId))
        {
            correlationId = Guid.NewGuid();
        }

        var richObservation = await CaptureCommandObservationAsync(
            new CommandObservationRequest
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                OriginalUserCommand = request.UserInput,
                BaselineObservation = observation,
                PrimaryTargetGrounding = primaryTargetGrounding,
                SafetyDisposition = safetyDisposition,
                SafetyRiskLevel = riskLevel
            },
            cancellationToken);

        return new AgentExecutionContext
        {
            CorrelationId = correlationId,
            RawInput = request.UserInput,
            NormalizedInput = request.UserInput.Trim().ToLowerInvariant(),
            DetectedIntent = DetectIntentKind(
                request.UserInput.Trim().ToLowerInvariant(),
                targets,
                primaryTargetGrounding,
                decision: null),
            CreatedAtUtc = request.RequestedAtUtc,
            SessionId = null,
            Observation = observation,
            RichObservation = richObservation,
            ContextAdapter = adapterContext,
            PrimaryTargetGrounding = primaryTargetGrounding,
            RuntimeState = null,
            ResolvedTargets = targets,
            Metadata = BuildExecutionContextMetadata(adapterContext, primaryTargetGrounding)
        };
    }

    private async Task<AgentExecutionContext> EnsurePrimaryTargetGroundingAsync(
        CommandRequest request,
        AgentExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.PrimaryTargetGrounding is not null || _targetGrounder is null)
        {
            return context;
        }

        try
        {
            var primaryTargetGrounding = await _targetGrounder.GroundAsync(request, context.ResolvedTargets, cancellationToken);
            return new AgentExecutionContext
            {
                CorrelationId = context.CorrelationId,
                RawInput = context.RawInput,
                NormalizedInput = context.NormalizedInput,
                DetectedIntent = DetectIntentKind(
                    context.NormalizedInput,
                    context.ResolvedTargets,
                    primaryTargetGrounding,
                    decision: null),
                CreatedAtUtc = context.CreatedAtUtc,
                SessionId = context.SessionId,
                Observation = context.Observation,
                RichObservation = context.RichObservation,
                ContextAdapter = context.ContextAdapter,
                PrimaryTargetGrounding = primaryTargetGrounding,
                RuntimeState = context.RuntimeState,
                ResolvedTargets = context.ResolvedTargets,
                Metadata = BuildExecutionContextMetadata(context.ContextAdapter, primaryTargetGrounding)
            };
        }
        catch
        {
            return context;
        }
    }

    private static IDictionary<string, string>? BuildExecutionContextMetadata(
        ContextAdapterContext? adapterContext,
        TargetGroundingResult? primaryTargetGrounding)
    {
        if (adapterContext is null && primaryTargetGrounding is null)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (adapterContext is not null)
        {
            metadata["contextAdapterMatch"] = "true";
            metadata["contextAdapter"] = adapterContext.AdapterName;

            if (!string.IsNullOrWhiteSpace(adapterContext.ProcessName))
            {
                metadata["contextAdapterProcess"] = adapterContext.ProcessName;
            }

            if (!string.IsNullOrWhiteSpace(adapterContext.WindowTitle))
            {
                metadata["contextAdapterWindowTitle"] = adapterContext.WindowTitle;
            }

            if (adapterContext.WindowHandle.HasValue)
            {
                metadata["contextAdapterWindowHandle"] = adapterContext.WindowHandle.Value.ToString();
            }

            if (adapterContext.Metadata is not null)
            {
                foreach (var kvp in adapterContext.Metadata)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        metadata[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        if (primaryTargetGrounding is not null)
        {
            metadata["primaryTargetGroundingDisposition"] = primaryTargetGrounding.Disposition.ToString();
            metadata["primaryGroundingInputSource"] = primaryTargetGrounding.InputSource.ToString();
            metadata["primaryGroundedTargetKind"] = primaryTargetGrounding.Target.Kind.ToString();
            metadata["primaryGroundingReason"] = primaryTargetGrounding.Reason.ToString();
            metadata["primaryGroundingExecutionSuitability"] = primaryTargetGrounding.ExecutionSuitability.ToString();

            if (!string.IsNullOrWhiteSpace(primaryTargetGrounding.Target.CanonicalValue))
            {
                metadata["primaryGroundedCanonicalValue"] = primaryTargetGrounding.Target.CanonicalValue;
            }
        }

        return metadata;
    }

    private async Task<CommandObservationSnapshot> CaptureCommandObservationAsync(
        CommandObservationRequest request,
        CancellationToken cancellationToken)
    {
        if (_commandObservationCollector is not null)
        {
            try
            {
                var collected = await _commandObservationCollector.CollectAsync(request, cancellationToken);
                if (collected is not null)
                {
                    return collected;
                }
            }
            catch
            {
                return CreateFallbackCommandObservationSnapshot(
                    request,
                    ObservationCollectionStatus.Failed,
                    "Command observation collector failed.");
            }
        }

        return CreateFallbackCommandObservationSnapshot(
            request,
            ObservationCollectionStatus.Partial,
            "Command observation collector unavailable. Used baseline observation context.");
    }

    private static CommandObservationSnapshot CreateFallbackCommandObservationSnapshot(
        CommandObservationRequest request,
        ObservationCollectionStatus status,
        string message)
    {
        var baselineWindow = request.BaselineObservation?.ActiveWindow;
        var fallbackProcessName = !string.IsNullOrWhiteSpace(request.BaselineObservation?.ActiveProcessName)
            ? request.BaselineObservation!.ActiveProcessName
            : baselineWindow?.ProcessName;

        ForegroundWindowObservation? windowObservation = null;
        if (baselineWindow is not null)
        {
            windowObservation = new ForegroundWindowObservation
            {
                Title = baselineWindow.Title,
                Handle = baselineWindow.Handle,
                IsForeground = baselineWindow.IsForeground
            };
        }

        ForegroundProcessObservation? processObservation = null;
        if (!string.IsNullOrWhiteSpace(fallbackProcessName))
        {
            processObservation = new ForegroundProcessObservation
            {
                Name = fallbackProcessName,
                ProcessId = null
            };
        }

        return new CommandObservationSnapshot
        {
            TimestampUtc = request.TimestampUtc,
            OriginalUserCommand = request.OriginalUserCommand,
            ForegroundWindow = windowObservation,
            ForegroundProcess = processObservation,
            GroundingSummary = request.PrimaryTargetGrounding is null
                ? null
                : new GroundingObservationSummary
                {
                    Disposition = request.PrimaryTargetGrounding.Disposition,
                    InputSource = request.PrimaryTargetGrounding.InputSource,
                    TargetKind = request.PrimaryTargetGrounding.Target.Kind,
                    CanonicalValue = string.IsNullOrWhiteSpace(request.PrimaryTargetGrounding.Target.CanonicalValue)
                        ? null
                        : request.PrimaryTargetGrounding.Target.CanonicalValue,
                    Reason = request.PrimaryTargetGrounding.Reason
                },
            SafetySummary = new SafetyObservationSummary
            {
                Disposition = request.SafetyDisposition,
                RiskLevel = request.SafetyRiskLevel,
                RequiresApproval = request.SafetyDisposition == SafetyDisposition.RequiresApproval
            },
            CollectionStatus = status,
            CollectionMessage = message
        };
    }

    private static void EnrichMetadataWithCommandObservation(
        IDictionary<string, string> metadata,
        CommandObservationSnapshot? observation)
    {
        if (observation is null)
        {
            return;
        }

        metadata["observation_status"] = observation.CollectionStatus.ToString();
        metadata["observation_secondary_enrichment"] = "true";
        metadata["observation_timestamp_utc"] = observation.TimestampUtc.ToString("O");
        metadata["observation_role_operational_fields"] = ObservationRolePartition.OperationalFields;
        metadata["observation_role_descriptive_fields"] = ObservationRolePartition.DescriptiveFields;

        if (!string.IsNullOrWhiteSpace(observation.CollectionMessage))
        {
            metadata["observation_message"] = observation.CollectionMessage;
        }

        if (!string.IsNullOrWhiteSpace(observation.OriginalUserCommand))
        {
            metadata["observation_command_text"] = observation.OriginalUserCommand;
        }

        if (!string.IsNullOrWhiteSpace(observation.ForegroundWindow?.Title))
        {
            metadata["observation_foreground_window_title"] = observation.ForegroundWindow.Title;
        }

        if (observation.ForegroundWindow?.Handle is long windowHandle)
        {
            metadata["observation_foreground_window_handle"] = windowHandle.ToString();
        }

        metadata["observation_foreground_window_is_foreground"] = observation.ForegroundWindow?.IsForeground == true
            ? "true"
            : "false";

        if (!string.IsNullOrWhiteSpace(observation.ForegroundProcess?.Name))
        {
            metadata["observation_foreground_process_name"] = observation.ForegroundProcess.Name;
        }

        if (observation.ForegroundProcess?.ProcessId is int processId)
        {
            metadata["observation_foreground_process_id"] = processId.ToString();
        }

        if (observation.GroundingSummary is not null)
        {
            metadata["observation_grounding_disposition"] = observation.GroundingSummary.Disposition.ToString();
            metadata["observation_grounding_input_source"] = observation.GroundingSummary.InputSource.ToString();
            metadata["observation_grounding_target_kind"] = observation.GroundingSummary.TargetKind.ToString();
            metadata["observation_grounding_reason"] = observation.GroundingSummary.Reason.ToString();

            if (!string.IsNullOrWhiteSpace(observation.GroundingSummary.CanonicalValue))
            {
                metadata["observation_grounding_canonical_value"] = observation.GroundingSummary.CanonicalValue;
            }
        }

        if (observation.SafetySummary is not null)
        {
            metadata["observation_safety_disposition"] = observation.SafetySummary.Disposition.ToString();
            metadata["observation_safety_risk"] = observation.SafetySummary.RiskLevel.ToString();
            metadata["observation_requires_approval"] = observation.SafetySummary.RequiresApproval ? "true" : "false";
        }

        if (observation.RuntimeState is not null)
        {
            EnrichMetadataWithRuntimeState(metadata, observation.RuntimeState);
        }

        if (!string.IsNullOrWhiteSpace(observation.CollectionMessage))
        {
            metadata["observation_collection_message_secondary"] = "true";
        }
    }

    private static IDictionary<string, string>? BuildAuditMetadata(
        AgentExecutionContext? context,
        IDictionary<string, string>? extraMetadata)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (context?.RichObservation is not null)
        {
            EnrichMetadataWithCommandObservation(metadata, context.RichObservation);
        }

        if (context is not null && !string.IsNullOrWhiteSpace(context.Observation?.ActiveProcessName))
        {
            metadata["activeProcess"] = context.Observation.ActiveProcessName;
        }

        if (context is not null && !string.IsNullOrWhiteSpace(context.Observation?.ActiveWindow?.Title))
        {
            metadata["activeWindowTitle"] = context.Observation.ActiveWindow.Title;
        }

        if (context is not null && context.Observation?.ActiveWindow?.Handle is long activeWindowHandle)
        {
            metadata["activeWindowHandle"] = activeWindowHandle.ToString();
        }

        if (context is not null && context.Observation?.ActiveWindow is not null)
        {
            metadata["activeWindowIsForeground"] = context.Observation.ActiveWindow.IsForeground ? "true" : "false";
        }

        if (context is not null && !string.IsNullOrWhiteSpace(context.Observation?.DesktopStateSummary))
        {
            metadata["desktopStateSummary"] = context.Observation.DesktopStateSummary;
            metadata["desktopStateSummary_secondary"] = "true";
        }

        if (context is not null)
        {
            metadata["observationCapturedAtUtc"] = context.Observation?.CapturedAtUtc.ToString("O") ?? string.Empty;
            metadata["observationCapturedAtUtc_secondary"] = "true";
        }

        if (context is not null && context.ResolvedTargets.Count > 0)
        {
            metadata["resolvedTargets"] = string.Join(", ",
                context.ResolvedTargets.Select(t => $"{t.Kind}:{t.NormalizedValue}"));

            metadata["resolvedTargetReasons"] = string.Join(", ",
                context.ResolvedTargets.Select(t => $"{t.Kind}:{t.ResolutionReasonKind}"));

            var firstResolvedTarget = context.ResolvedTargets[0];
            metadata["target_resolution_reason"] = firstResolvedTarget.ResolutionReasonKind.ToString();

            if (!string.IsNullOrWhiteSpace(firstResolvedTarget.ResolutionSourceText))
            {
                metadata["target_resolution_source_text"] = firstResolvedTarget.ResolutionSourceText;
            }
        }

        if (context is not null && context.DetectedIntent != CommandIntentKind.Unknown)
        {
            metadata["detected_intent"] = context.DetectedIntent.ToString();
        }

        if (context?.RuntimeState is not null)
        {
            EnrichMetadataWithRuntimeState(metadata, context.RuntimeState);
        }

        if (context?.Metadata is not null)
        {
            foreach (var kvp in context.Metadata)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        if (extraMetadata is not null)
        {
            foreach (var kvp in extraMetadata)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        var provenanceConsistency = ContextProvenanceConsistencyHelper.Evaluate(context, metadata);
        metadata["context_provenance_consistent"] = provenanceConsistency.IsConsistent ? "true" : "false";
        metadata["context_provenance_source"] = provenanceConsistency.Source;

        if (metadata.Count == 0)
        {
            return null;
        }

        return metadata;
    }

    private static IDictionary<string, string> BuildPolicyEvaluationMetadata(
        string scope,
        StepSafetyRequest? stepSafetyRequest = null,
        StepSafetyDecision? stepSafetyDecision = null,
        bool approvalSnapshotMatch = false,
        string? approvalAuthorityOutcome = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["policy_scope"] = scope
        };

        if (stepSafetyRequest is not null)
        {
            metadata["step_policy_source"] = stepSafetyRequest.Source.ToString();
            metadata["step_policy_step_index"] = stepSafetyRequest.StepIndex.ToString();

            var actionType = stepSafetyRequest.ExecuteAction?.ActionType.ToString();
            if (string.IsNullOrWhiteSpace(actionType) && stepSafetyRequest.AgentAction is not null)
            {
                actionType = stepSafetyRequest.AgentAction.ActionName;
            }

            if (!string.IsNullOrWhiteSpace(actionType))
            {
                metadata["step_policy_action_type"] = actionType;
            }

            var targetReference = stepSafetyRequest.ExecuteAction?.Target.Reference ??
                                  stepSafetyRequest.AgentAction?.Target?.NormalizedValue ??
                                  stepSafetyRequest.AgentAction?.Target?.DisplayName ??
                                  stepSafetyRequest.AgentAction?.Target?.OriginalText;
            if (!string.IsNullOrWhiteSpace(targetReference))
            {
                metadata["step_policy_target"] = targetReference;
            }
        }

        if (stepSafetyDecision is not null)
        {
            metadata["step_policy_disposition"] = stepSafetyDecision.Disposition.ToString();
            metadata["step_policy_risk"] = stepSafetyDecision.RiskLevel.ToString();
            metadata["step_policy_approval_key_present"] = string.IsNullOrWhiteSpace(stepSafetyDecision.ApprovalKey)
                ? "false"
                : "true";
        }

        if (!string.IsNullOrWhiteSpace(approvalAuthorityOutcome))
        {
            metadata["approval_authority_outcome"] = approvalAuthorityOutcome;
        }

        metadata["approval_snapshot_match"] = approvalSnapshotMatch ? "true" : "false";
        return metadata;
    }

    private static string? DetermineApprovalAuthorityOutcome(
        bool modelRequestedApproval,
        StepSafetyDecision stepSafetyDecision)
    {
        if (!modelRequestedApproval)
        {
            return null;
        }

        return stepSafetyDecision.Disposition switch
        {
            SafetyDisposition.Allowed => "model-requested-but-policy-allowed",
            SafetyDisposition.RequiresApproval => "policy-required-approval",
            SafetyDisposition.Denied => "policy-denied",
            _ => null
        };
    }

    private static string DetermineExecutionMode(string toolName, ToolResult toolResult)
    {
        if (toolName.Equals("SearchWebTool", StringComparison.OrdinalIgnoreCase))
        {
            return "simulated";
        }

        if (toolResult.PrimitiveExecution is not null)
        {
            if (!toolResult.PrimitiveExecution.Success &&
                !string.IsNullOrWhiteSpace(toolResult.PrimitiveExecution.BlockedReason))
            {
                return "blocked";
            }

            return toolResult.PrimitiveExecution.Success ? "real" : "failed";
        }

        if (toolResult.Output.StartsWith("Real launch attempted", StringComparison.OrdinalIgnoreCase))
        {
            return "real";
        }

        if (toolResult.Output.StartsWith("Blocked execution", StringComparison.OrdinalIgnoreCase))
        {
            return "blocked";
        }

        if (toolResult.Message.Contains("simulation mode", StringComparison.OrdinalIgnoreCase))
        {
            return "simulated";
        }

        return "unknown";
    }
}
