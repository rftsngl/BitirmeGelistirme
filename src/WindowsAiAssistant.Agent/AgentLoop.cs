using System.Text.Json;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Logging;
using WindowsAiAssistant.Runtime.Observation;

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
        "press_shortcut"
    };

    private static readonly JsonSerializerOptions LogJsonOptions = new() { WriteIndented = false };

    private readonly AgentOptions _options;
    private readonly ObservationService _observationService;
    private readonly PromptBuilder _promptBuilder;
    private readonly AiClient _aiClient;
    private readonly DecisionParser _decisionParser;
    private readonly ActionExecutor _actionExecutor;
    private readonly RunLogger _runLogger;

    public AgentLoop(
        AgentOptions options,
        ObservationService observationService,
        PromptBuilder promptBuilder,
        AiClient aiClient,
        DecisionParser decisionParser,
        ActionExecutor actionExecutor,
        RunLogger runLogger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _observationService = observationService ?? throw new ArgumentNullException(nameof(observationService));
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        _decisionParser = decisionParser ?? throw new ArgumentNullException(nameof(decisionParser));
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _runLogger = runLogger ?? throw new ArgumentNullException(nameof(runLogger));
    }

    public async Task<AgentLoopResult> RunAsync(
        string userGoal,
        CancellationToken cancellationToken = default,
        IProgress<AgentStepProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userGoal);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new AgentSession
        {
            RunId = Guid.NewGuid().ToString("N"),
            UserGoal = userGoal.Trim()
        };

        var maxSteps = Math.Clamp(_options.MaxSteps, 1, 20);
        Report(progress, 0, maxSteps, "basladi", session.UserGoal);

        DesktopObservation? lastObservation = null;

        for (var stepIndex = 0; stepIndex < maxSteps; stepIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastStep = session.Steps.LastOrDefault();
            Report(progress, stepIndex, maxSteps, "gozlem", "Masaustu durumu aliniyor");

            var observation = await _observationService.CaptureAsync(
                new ObservationCaptureOptions
                {
                    LastUserGoal = session.UserGoal,
                    LastActionResult = lastStep?.ActionResult?.Message
                },
                cancellationToken).ConfigureAwait(false);
            lastObservation = observation;

            var prompt = _promptBuilder.Build(session.UserGoal, observation, session.Steps);
            Report(progress, stepIndex, maxSteps, "llm", $"Adim {stepIndex + 1} karar isteniyor");

            var llmContent = await RequestLlmDecisionAsync(prompt, stepIndex, cancellationToken).ConfigureAwait(false);
            if (llmContent.Error is not null)
            {
                return Fail(session, llmContent.Error, lastObservation);
            }

            var parseResult = _decisionParser.Parse(llmContent.Content!);
            if (!parseResult.Success)
            {
                var retryPrompt = prompt + Environment.NewLine +
                    "Your previous reply was invalid. Return ONLY one valid JSON object matching the schema.";
                llmContent = await RequestLlmDecisionAsync(retryPrompt, stepIndex, cancellationToken).ConfigureAwait(false);
                if (llmContent.Error is not null)
                {
                    return Fail(session, llmContent.Error, lastObservation);
                }

                parseResult = _decisionParser.Parse(llmContent.Content!);
            }

            if (!parseResult.Success || parseResult.Decision is null)
            {
                return Fail(session, parseResult.ErrorMessage ?? "Karar okunamadi.", lastObservation);
            }

            var decision = parseResult.Decision;
            Report(progress, stepIndex, maxSteps, "execute", decision.Action);

            var actionResult = await _actionExecutor
                .ExecuteAsync(decision.ToAgentAction(), cancellationToken)
                .ConfigureAwait(false);

            session.Steps.Add(new AgentStep
            {
                Index = stepIndex,
                LlmRawOutput = llmContent.Content,
                ParsedDecision = decision,
                ActionResult = actionResult
            });

            await _runLogger.AppendAsync(
                new AgentRunLog
                {
                    RunId = session.RunId,
                    StepIndex = stepIndex,
                    UserGoal = session.UserGoal,
                    ObservationSummaryJson = observation.ToJsonSummary(),
                    LlmRawOutput = llmContent.Content,
                    ParsedDecisionJson = JsonSerializer.Serialize(decision, LogJsonOptions),
                    ActionResultJson = JsonSerializer.Serialize(actionResult, LogJsonOptions),
                    Timestamp = DateTimeOffset.UtcNow
                },
                cancellationToken).ConfigureAwait(false);

            if (!actionResult.Success)
            {
                return Fail(session, actionResult.Message, lastObservation);
            }

            if (!ShouldContinueLoop(decision))
            {
                session.IsComplete = true;
                var message = ResolveAssistantMessage(decision, actionResult);
                Report(progress, stepIndex, maxSteps, "tamamlandi", message);
                return Complete(session, message, lastObservation, reachedMaxSteps: false);
            }
        }

        session.IsComplete = true;
        var lastMessage = session.Steps.LastOrDefault()?.ActionResult?.Message ??
                          "Maksimum adim sayisina ulasildi. Kismi tamamlama.";
        Report(progress, maxSteps - 1, maxSteps, "limit", lastMessage);
        return Complete(session, lastMessage, lastObservation, reachedMaxSteps: true);
    }

    private AgentLoopResult Fail(AgentSession session, string errorMessage, DesktopObservation? observation) =>
        new()
        {
            Session = session,
            Success = false,
            ErrorMessage = errorMessage,
            ObservationSummary = observation?.ToShortSummary(),
            LogFilePath = _runLogger.GetLogFilePath(session.RunId)
        };

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
        int stepIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await _aiClient.GetDecisionAsync(prompt, cancellationToken).ConfigureAwait(false);
            return (content, null);
        }
        catch (AiClientException ex)
        {
            return (null, ex.Message);
        }
    }

    private static bool ShouldContinueLoop(AgentDecision decision)
    {
        if (decision.IsComplete)
        {
            return false;
        }

        if (decision.DecisionType is AgentDecisionType.Stop or AgentDecisionType.Complete or AgentDecisionType.AskUser)
        {
            return false;
        }

        if (ContinueActions.Contains(decision.Action))
        {
            return true;
        }

        return !decision.Action.Equals("respond", StringComparison.OrdinalIgnoreCase) &&
               !decision.Action.Equals("ask_user", StringComparison.OrdinalIgnoreCase) &&
               !decision.Action.Equals("stop", StringComparison.OrdinalIgnoreCase);
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
