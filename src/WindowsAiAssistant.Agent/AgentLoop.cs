using System.Text;
using System.Text.Json;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Automation;
using WindowsAiAssistant.Runtime.Debugging;
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
        "mouse_drag",
        "shell",
        "capture_screen",
        "notify",
        "wmi_query",
        "schedule_task",
        "jump_list",
        "com_invoke",
        "verify_user",
        "global_hook",
        "service_control",
        "event_log",
        "registry_op",
        "clipboard",
        "install_package",
        "network_status",
        "audio_power",
        "perf_counter",
        "file_search",
        "notification_listen",
        "shell_session",
        "file_watch",
        "credential_store"
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

        var maxSteps = Math.Clamp(_options.MaxSteps, 1, 40);
        _actionGate.BeginSession();
        Report(progress, 0, maxSteps, "basladi", session.UserGoal);

        DesktopObservation? lastObservation = null;

        for (var stepIndex = 0; stepIndex < maxSteps; stepIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastStep = session.Steps.LastOrDefault();
            Report(progress, stepIndex, maxSteps, "gozlem", "Masaustu durumu ve ekran goruntusu aliniyor");

            var previousObservation = lastObservation;
            var observation = await _observationService.CaptureAsync(
                new ObservationCaptureOptions
                {
                    LastUserGoal = session.UserGoal,
                    LastActionResult = lastStep?.ActionResult?.Message,
                    RunId = session.RunId,
                    StepIndex = stepIndex,
                    PreviousActiveWindowTitle = previousObservation?.ActiveWindowTitle,
                    PreviousActiveProcessName = previousObservation?.ActiveProcessName
                },
                cancellationToken).ConfigureAwait(false);
            lastObservation = observation;

            AgentDecision decision;
            string llmRawOutput = string.Empty;

            var fastDecision = GoalRoutingHints.TryBuildFastDecision(session.UserGoal);
            if (fastDecision is not null &&
                !session.Steps.Any(step =>
                    string.Equals(step.ParsedDecision?.Action, fastDecision.Action, StringComparison.OrdinalIgnoreCase)))
            {
                decision = fastDecision;
                llmRawOutput = JsonSerializer.Serialize(new
                {
                    fastRoute = fastDecision.Action,
                    mode = fastDecision.Parameters.GetValueOrDefault("mode")
                }, LogJsonOptions);
                Report(progress, stepIndex, maxSteps, "llm",
                    $"Hizli yol — dogrudan {fastDecision.Action}");
            }
            else
            {
                var prompt = _promptBuilder.Build(session.UserGoal, observation, session.Steps, triggerSource);
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
                            LlmRawOutput = llmRawOutput,
                            Timestamp = DateTimeOffset.UtcNow
                        },
                        cancellationToken).ConfigureAwait(false);

                    parseResult = _decisionParser.Parse(llmContent.Content!);
                }

                if (!parseResult.Success || parseResult.Decision is null)
                {
                    return Fail(session, parseResult.ErrorMessage ?? "Karar okunamadi.", lastObservation, AgentErrorKind.Decision);
                }

                decision = parseResult.Decision;
                llmRawOutput = llmContent.Content!;
            }

            var agentAction = decision.ToAgentAction();

            // #region agent log
            var isUiAction = UiElementIdValidator.IsUiAutomationAction(decision.Action) ||
                             decision.Action.Equals("mouse_click", StringComparison.OrdinalIgnoreCase);
            var targetLooksLikeElementId = UiElementIdValidator.IsValidFormat(decision.Target);
            DebugAgentLog.Write(
                "H2",
                "AgentLoop.RunAsync",
                "llm decision parsed",
                new
                {
                    session.RunId,
                    stepIndex,
                    decision.Action,
                    decision.Target,
                    decisionType = decision.DecisionType.ToString(),
                    isUiAction,
                    targetLooksLikeElementId,
                    userGoal = session.UserGoal
                },
                session.RunId);
            if (isUiAction && !string.IsNullOrWhiteSpace(decision.Target) && !targetLooksLikeElementId)
            {
                DebugAgentLog.Write(
                    "H3",
                    "AgentLoop.RunAsync",
                    "ui action with non-elementId target",
                    new
                    {
                        session.RunId,
                        stepIndex,
                        decision.Action,
                        decision.Target
                    },
                    session.RunId);
            }
            // #endregion

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
                    Report(progress, stepIndex, maxSteps, "eylem", decision.Action);
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
                Report(progress, stepIndex, maxSteps, "eylem", decision.Action);
                actionResult = await _actionExecutor
                    .ExecuteAsync(agentAction, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!actionResult.Success && decision.Action is not ("respond" or "ask_user" or "stop"))
            {
                Report(progress, stepIndex, maxSteps, "eylem", $"{decision.Action}|fail|{actionResult.Message}");
            }

            session.Steps.Add(new AgentStep
            {
                Index = stepIndex,
                LlmRawOutput = llmRawOutput,
                ParsedDecision = decision,
                ActionResult = actionResult
            });

            // #region agent log
            DebugAgentLog.Write(
                "H4",
                "AgentLoop.RunAsync",
                "action executed",
                new
                {
                    session.RunId,
                    stepIndex,
                    decision.Action,
                    decision.Target,
                    actionResult.Success,
                    actionResult.Message,
                    gateOutcome = gateDecision.Outcome.ToString(),
                    userApproved
                },
                session.RunId);
            // #endregion

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
                    LlmRawOutput = llmRawOutput,
                    ParsedDecisionJson = JsonSerializer.Serialize(decision, LogJsonOptions),
                    GateDecisionJson = gateLogJson,
                    ActionResultJson = JsonSerializer.Serialize(actionResult, LogJsonOptions),
                    Timestamp = DateTimeOffset.UtcNow
                },
                cancellationToken).ConfigureAwait(false);

            // Otonom operatör: başarısız bir eylem ölü nokta değil, geri bildirimdir.
            // Hata mesajı sonraki gözlemin lastActionResult'una ve adım geçmişine düşer;
            // LLM bunu okuyup strateji değiştirebilir (shell ile keşif, başka aile, ya da
            // gerekirse respond). Döngü yalnızca maxSteps ile sınırlanır, ilk hatada durmaz.
            if (!actionResult.Success)
            {
                continue;
            }

            if (actionResult.Success &&
                GoalRoutingHints.ShouldCompleteAfterFastRoute(session.UserGoal, decision.Action))
            {
                session.IsComplete = true;
                var fastMessage = ResolveAssistantMessage(decision, actionResult);
                Report(progress, stepIndex, maxSteps, "tamamlandi", fastMessage);
                await _runLogger.AppendAsync(
                    new AgentRunLog
                    {
                        RunId = session.RunId,
                        StepIndex = stepIndex,
                        UserGoal = session.UserGoal,
                        ObservationSummaryJson = observation.ToJsonSummary(),
                        LlmRawOutput = llmRawOutput,
                        ParsedDecisionJson = JsonSerializer.Serialize(decision, LogJsonOptions),
                        ActionResultJson = JsonSerializer.Serialize(actionResult, LogJsonOptions),
                        Timestamp = DateTimeOffset.UtcNow
                    },
                    cancellationToken).ConfigureAwait(false);

                return Complete(session, fastMessage, observation, reachedMaxSteps: false);
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
        var maxStepsMessage =
            $"Maksimum adim sayisina ulasildi ({maxSteps} adim). Gorev tamamlanamadi; kismi sonuc.";
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

        var userMessage = TryGetLastRespondMessage(session)
            ?? await RequestFinalUserFeedbackAsync(session, lastObservation, cancellationToken).ConfigureAwait(false)
            ?? maxStepsMessage;
        Report(progress, maxSteps - 1, maxSteps, "limit", maxStepsMessage);
        return Complete(session, userMessage, lastObservation, reachedMaxSteps: true);
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

    private static string? TryGetLastRespondMessage(AgentSession session)
    {
        for (var i = session.Steps.Count - 1; i >= 0; i--)
        {
            var step = session.Steps[i];
            var action = step.ParsedDecision?.Action;
            if (action is not ("respond" or "ask_user"))
            {
                continue;
            }

            if (step.ActionResult?.Success != true)
            {
                continue;
            }

            if (step.ParsedDecision?.Parameters.TryGetValue("message", out var message) == true &&
                !string.IsNullOrWhiteSpace(message))
            {
                return message.Trim();
            }

            if (!string.IsNullOrWhiteSpace(step.ActionResult.Message))
            {
                return step.ActionResult.Message.Trim();
            }
        }

        return null;
    }

    private async Task<string?> RequestFinalUserFeedbackAsync(
        AgentSession session,
        DesktopObservation? observation,
        CancellationToken cancellationToken)
    {
        if (session.Steps.Count == 0)
        {
            return null;
        }

        var history = new StringBuilder();
        foreach (var step in session.Steps.OrderBy(s => s.Index))
        {
            var action = step.ParsedDecision?.Action ?? "?";
            var success = step.ActionResult?.Success == true ? "ok" : "fail";
            var resultMessage = step.ActionResult?.Message ?? "(no result)";
            history.AppendLine($"  - {action} -> {success}: {resultMessage}");
        }

        var prompt = new StringBuilder()
            .AppendLine("The agent loop reached max steps without a clean user-facing respond.")
            .AppendLine($"User goal: {session.UserGoal.Trim()}")
            .AppendLine("Steps taken:")
            .AppendLine(history.ToString())
            .AppendLine()
            .AppendLine("Reply with ONLY one JSON object matching the schema.")
            .AppendLine("Use decisionType=complete, action=respond, isComplete=true.")
            .AppendLine("parameters.message MUST be a concise Turkish summary for the user: what was tried and the outcome.")
            .AppendLine("Do not mention JSON, steps, or internal logs.")
            .ToString();

        var llmContent = await RequestLlmDecisionAsync(
                prompt,
                observation ?? new DesktopObservation(),
                cancellationToken)
            .ConfigureAwait(false);
        if (llmContent.Error is not null || string.IsNullOrWhiteSpace(llmContent.Content))
        {
            return null;
        }

        var parseResult = _decisionParser.Parse(llmContent.Content);
        if (!parseResult.Success || parseResult.Decision is null)
        {
            return null;
        }

        return ResolveAssistantMessage(parseResult.Decision, new ActionResult
        {
            Success = true,
            Message = parseResult.Decision.Parameters.TryGetValue("message", out var msg) ? msg : string.Empty
        });
    }

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
             decision.Action.Equals("ask_user", StringComparison.OrdinalIgnoreCase) ||
             decision.Action.Equals("audio_power", StringComparison.OrdinalIgnoreCase)))
        {
            return actionResult.Message;
        }

        if (!string.IsNullOrWhiteSpace(decision.Reason))
        {
            return decision.Reason.Trim();
        }

        return actionResult.Message;
    }

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
