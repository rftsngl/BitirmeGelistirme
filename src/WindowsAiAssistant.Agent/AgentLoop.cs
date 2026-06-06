using System.Text.Json;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Logging;
using WindowsAiAssistant.Runtime.Observation;
using WindowsAiAssistant.Runtime.Policy;

namespace WindowsAiAssistant.Agent;

public sealed class AgentLoop
{
    private static readonly HashSet<string> ContinueActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "wait",
        "open_app",
        "open_url",
        "type_text",
        "press_key",
        "press_shortcut",
        "click_element",
        "focus_element",
        "read_element",
        "set_value",
        "select_element",
        "expand_collapse",
        "invoke_toggle",
        "scroll",
        "focus_window",
        "window_state",
        "move_window",
        "list_windows",
        "launch",
        "mouse_click",
        "mouse_scroll",
        "mouse_drag"
    };

    private static readonly JsonSerializerOptions LogJsonOptions = new() { WriteIndented = false };

    private readonly AgentOptions _options;
    private readonly ObservationService _observationService;
    private readonly PromptBuilder _promptBuilder;
    private readonly AiClient _aiClient;
    private readonly DecisionParser _decisionParser;
    private readonly ActionExecutor _actionExecutor;
    private readonly ActionGate _actionGate;
    private readonly IActionApprovalHandler _approvalHandler;
    private readonly RunLogger _runLogger;

    public AgentLoop(
        AgentOptions options,
        ObservationService observationService,
        PromptBuilder promptBuilder,
        AiClient aiClient,
        DecisionParser decisionParser,
        ActionExecutor actionExecutor,
        ActionGate actionGate,
        IActionApprovalHandler approvalHandler,
        RunLogger runLogger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _observationService = observationService ?? throw new ArgumentNullException(nameof(observationService));
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        _decisionParser = decisionParser ?? throw new ArgumentNullException(nameof(decisionParser));
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _actionGate = actionGate ?? throw new ArgumentNullException(nameof(actionGate));
        _approvalHandler = approvalHandler ?? throw new ArgumentNullException(nameof(approvalHandler));
        _runLogger = runLogger ?? throw new ArgumentNullException(nameof(runLogger));
    }

    public async Task<AgentLoopResult> RunAsync(
        string userGoal,
        CancellationToken cancellationToken = default,
        IProgress<AgentStepProgress>? progress = null,
        string? triggerSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userGoal);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new AgentSession
        {
            RunId = Guid.NewGuid().ToString("N"),
            UserGoal = userGoal.Trim()
        };

        var maxSteps = Math.Clamp(_options.MaxSteps, 1, 20);
        _actionGate.BeginSession();
        Report(progress, 0, maxSteps, "basladi", session.UserGoal);

        DesktopObservation? lastObservation = null;

        for (var stepIndex = 0; stepIndex < maxSteps; stepIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastStep = session.Steps.LastOrDefault();
            Report(progress, stepIndex, maxSteps, "gozlem", "Masaustu durumu ve ekran goruntusu aliniyor");

            var observation = await _observationService.CaptureAsync(
                new ObservationCaptureOptions
                {
                    LastUserGoal = session.UserGoal,
                    LastActionResult = lastStep?.ActionResult?.Message,
                    RunId = session.RunId,
                    StepIndex = stepIndex
                },
                cancellationToken).ConfigureAwait(false);
            lastObservation = observation;

            var prompt = _promptBuilder.Build(session.UserGoal, observation, session.Steps);
            Report(progress, stepIndex, maxSteps, "llm", $"Adim {stepIndex + 1} karar isteniyor");

            var llmContent = await RequestLlmDecisionAsync(prompt, observation, cancellationToken).ConfigureAwait(false);
            if (llmContent.Error is not null)
            {
                return Fail(session, llmContent.Error, lastObservation, AgentErrorKind.Provider);
            }

            var parseResult = _decisionParser.Parse(llmContent.Content!);
            if (!parseResult.Success)
            {
                var retryReason = parseResult.ErrorMessage ?? "bilinmeyen sebep";
                var retryPrompt = prompt + Environment.NewLine +
                    $"Your previous reply was invalid ({retryReason}). Return ONLY one valid JSON object matching the schema.";
                llmContent = await RequestLlmDecisionAsync(retryPrompt, observation, cancellationToken).ConfigureAwait(false);
                if (llmContent.Error is not null)
                {
                    return Fail(session, llmContent.Error, lastObservation, AgentErrorKind.Provider);
                }

                await _runLogger.AppendAsync(
                    new AgentRunLog
                    {
                        RunId = session.RunId,
                        StepIndex = stepIndex,
                        UserGoal = session.UserGoal,
                        ObservationSummaryJson = $"{{\"parseRetryReason\":\"{Escape(retryReason)}\"}}",
                        LlmRawOutput = llmContent.Content,
                        Timestamp = DateTimeOffset.UtcNow
                    },
                    cancellationToken).ConfigureAwait(false);

                parseResult = _decisionParser.Parse(llmContent.Content!);
            }

            if (!parseResult.Success || parseResult.Decision is null)
            {
                return Fail(session, parseResult.ErrorMessage ?? "Karar okunamadi.", lastObservation, AgentErrorKind.Decision);
            }

            var decision = parseResult.Decision;
            var agentAction = decision.ToAgentAction();
            var gateDecision = _actionGate.Evaluate(agentAction);
            Report(progress, stepIndex, maxSteps, "gate", gateDecision.Summary);

            bool? userApproved = null;
            ActionResult actionResult;

            if (gateDecision.Outcome == GateOutcome.Deny)
            {
                actionResult = new ActionResult
                {
                    Success = false,
                    Message = $"ActionGate engelledi: {gateDecision.Reason}"
                };
            }
            else if (gateDecision.Outcome == GateOutcome.RequireApproval)
            {
                Report(progress, stepIndex, maxSteps, "onay", gateDecision.Summary);
                userApproved = await _approvalHandler
                    .RequestApprovalAsync(gateDecision, agentAction, cancellationToken)
                    .ConfigureAwait(false);

                if (userApproved == true)
                {
                    actionResult = await _actionExecutor
                        .ExecuteAsync(agentAction, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    actionResult = new ActionResult
                    {
                        Success = false,
                        Message = $"Kullanici islemi reddetti: {gateDecision.Summary}"
                    };
                }
            }
            else
            {
                actionResult = await _actionExecutor
                    .ExecuteAsync(agentAction, cancellationToken)
                    .ConfigureAwait(false);
            }

            session.Steps.Add(new AgentStep
            {
                Index = stepIndex,
                LlmRawOutput = llmContent.Content,
                ParsedDecision = decision,
                ActionResult = actionResult
            });

            var gateLogJson = SerializeGateLog(gateDecision, userApproved);

            await _runLogger.AppendAsync(
                new AgentRunLog
                {
                    RunId = session.RunId,
                    StepIndex = stepIndex,
                    UserGoal = session.UserGoal,
                    TriggerSource = triggerSource,
                    ObservationSummaryJson = observation.ToJsonSummary(),
                    ScreenshotPath = observation.Screenshot?.FilePath,
                    WindowsSummaryJson = observation.WindowsToJson(),
                    UiTreeSummaryJson = observation.UiTreeToJson(),
                    LlmRawOutput = llmContent.Content,
                    ParsedDecisionJson = JsonSerializer.Serialize(decision, LogJsonOptions),
                    GateDecisionJson = gateLogJson,
                    ActionResultJson = JsonSerializer.Serialize(actionResult, LogJsonOptions),
                    Timestamp = DateTimeOffset.UtcNow
                },
                cancellationToken).ConfigureAwait(false);

            if (!actionResult.Success && !IsRecoverableGateFailure(gateDecision, userApproved))
            {
                return Fail(session, actionResult.Message, lastObservation, AgentErrorKind.Action);
            }

            if (!actionResult.Success)
            {
                continue;
            }

            if (!ShouldContinueLoop(decision, out var stopReason))
            {
                session.IsComplete = true;
                var message = ResolveAssistantMessage(decision, actionResult);
                Report(progress, stepIndex, maxSteps, "tamamlandi", message);
                await _runLogger.AppendAsync(
                    new AgentRunLog
                    {
                        RunId = session.RunId,
                        StepIndex = stepIndex,
                        UserGoal = session.UserGoal,
                        ObservationSummaryJson = $"{{\"loopStop\":\"{Escape(stopReason)}\"}}",
                        Timestamp = DateTimeOffset.UtcNow
                    },
                    cancellationToken).ConfigureAwait(false);
                return Complete(session, message, lastObservation, reachedMaxSteps: false);
            }
        }

        session.IsComplete = true;
        var maxStepsMessage = $"Maksimum adim sayisina ulasildi ({maxSteps} adim). Kismi sonuc.";
        await _runLogger.AppendAsync(
            new AgentRunLog
            {
                RunId = session.RunId,
                StepIndex = maxSteps,
                UserGoal = session.UserGoal,
                ObservationSummaryJson = $"{{\"loopStop\":\"maxSteps={maxSteps}\"}}",
                Timestamp = DateTimeOffset.UtcNow
            },
            cancellationToken).ConfigureAwait(false);
        var lastMessage = session.Steps.LastOrDefault()?.ActionResult?.Message ?? maxStepsMessage;
        Report(progress, maxSteps - 1, maxSteps, "limit", maxStepsMessage);
        return Complete(session, lastMessage, lastObservation, reachedMaxSteps: true);
    }

    private AgentLoopResult Fail(
        AgentSession session,
        string errorMessage,
        DesktopObservation? observation,
        AgentErrorKind errorKind = AgentErrorKind.Unexpected) =>
        new()
        {
            Session = session,
            Success = false,
            ErrorMessage = errorMessage,
            ErrorKind = errorKind,
            ObservationSummary = observation?.ToShortSummary(),
            LogFilePath = _runLogger.GetLogFilePath(session.RunId)
        };

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal)
             .Replace("\n", " ", StringComparison.Ordinal)
             .Replace("\r", " ", StringComparison.Ordinal);

    private AgentLoopResult Complete(
        AgentSession session,
        string assistantMessage,
        DesktopObservation? observation,
        bool reachedMaxSteps) =>
        new()
        {
            Session = session,
            Success = true,
            AssistantMessage = assistantMessage,
            ReachedMaxSteps = reachedMaxSteps,
            ObservationSummary = observation?.ToShortSummary(),
            LogFilePath = _runLogger.GetLogFilePath(session.RunId)
        };

    private async Task<(string? Content, string? Error)> RequestLlmDecisionAsync(
        string prompt,
        DesktopObservation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await _aiClient
                .GetDecisionAsync(prompt, observation.Screenshot?.Base64Png, cancellationToken)
                .ConfigureAwait(false);
            return (content, null);
        }
        catch (AiClientException ex)
        {
            return (null, ex.Message);
        }
    }

    private static bool ShouldContinueLoop(AgentDecision decision, out string stopReason)
    {
        if (decision.IsComplete)
        {
            stopReason = "isComplete=true";
            return false;
        }

        if (decision.DecisionType is AgentDecisionType.Stop or AgentDecisionType.Complete or AgentDecisionType.AskUser)
        {
            stopReason = $"decisionType={decision.DecisionType}";
            return false;
        }

        if (ContinueActions.Contains(decision.Action))
        {
            stopReason = string.Empty;
            return true;
        }

        var terminates = decision.Action.Equals("respond", StringComparison.OrdinalIgnoreCase) ||
                         decision.Action.Equals("ask_user", StringComparison.OrdinalIgnoreCase) ||
                         decision.Action.Equals("stop", StringComparison.OrdinalIgnoreCase);
        if (terminates)
        {
            stopReason = $"action={decision.Action}";
            return false;
        }

        stopReason = $"unrecognised-action={decision.Action}";
        return false;
    }

    private static string ResolveAssistantMessage(AgentDecision decision, ActionResult actionResult)
    {
        if (decision.Parameters.TryGetValue("message", out var message) &&
            !string.IsNullOrWhiteSpace(message))
        {
            return message.Trim();
        }

        if (!string.IsNullOrWhiteSpace(actionResult.Message) &&
            (decision.Action.Equals("respond", StringComparison.OrdinalIgnoreCase) ||
             decision.Action.Equals("ask_user", StringComparison.OrdinalIgnoreCase)))
        {
            return actionResult.Message;
        }

        if (!string.IsNullOrWhiteSpace(decision.Reason))
        {
            return decision.Reason.Trim();
        }

        return actionResult.Message;
    }

    private static bool IsRecoverableGateFailure(GateDecision gateDecision, bool? userApproved) =>
        gateDecision.Outcome == GateOutcome.Deny ||
        (gateDecision.Outcome == GateOutcome.RequireApproval && userApproved != true);

    private static string SerializeGateLog(GateDecision gateDecision, bool? userApproved) =>
        JsonSerializer.Serialize(new
        {
            outcome = gateDecision.Outcome.ToString(),
            risk = gateDecision.Risk.ToString(),
            reason = gateDecision.Reason,
            summary = gateDecision.Summary,
            approvalKey = gateDecision.ApprovalKey,
            userApproved
        }, LogJsonOptions);

    private static void Report(
        IProgress<AgentStepProgress>? progress,
        int stepIndex,
        int maxSteps,
        string phase,
        string? detail)
    {
        progress?.Report(new AgentStepProgress
        {
            StepIndex = stepIndex,
            MaxSteps = maxSteps,
            Phase = phase,
            Detail = detail
        });
    }
}
