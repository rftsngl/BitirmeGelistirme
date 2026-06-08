using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Planning;

public sealed class PlanningPhaseService
{
    private const string PlanningSystemPrompt =
        "You produce concise execution plans for a Windows desktop operator. " +
        "Output strict JSON only. Plans must minimize steps and reuse open windows. " +
        "Turkish in summary/intent fields; JSON keys stay English. " +
        "Set domain to one of: conversation, integration, office, window, generic_desktop.";

    private readonly AgentOptions _options;
    private readonly AiClient _aiClient;
    private readonly ExecutionPlanParser _parser;

    public PlanningPhaseService(AgentOptions options, AiClient aiClient, ExecutionPlanParser parser)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<ExecutionPlan?> TryCreatePlanAsync(
        string userGoal,
        DesktopObservation observation,
        CancellationToken cancellationToken = default)
    {
        if (!_options.PlanningEnabled || !PlanningEligibility.ShouldPlan(userGoal))
        {
            return null;
        }

        return await RequestPlanAsync(PlanningPromptBuilder.Build(userGoal, observation), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ExecutionPlan?> TryRevisePlanAsync(
        AgentSession session,
        DesktopObservation observation,
        string revisionReason,
        CancellationToken cancellationToken = default)
    {
        if (!_options.PlanRevisionEnabled || session.ExecutionPlan is null)
        {
            return null;
        }

        var prompt = PlanningPromptBuilder.BuildRevision(
            session.UserGoal,
            observation,
            session.ExecutionPlan,
            session.CurrentPlanStepIndex,
            session.Steps,
            revisionReason);

        var revised = await RequestPlanAsync(prompt, cancellationToken).ConfigureAwait(false);
        if (revised is null)
        {
            return null;
        }

        session.PlanRevisionCount++;
        session.CurrentPlanStepIndex = 0;
        session.ConsecutiveStepFailures = 0;
        return revised;
    }

    private async Task<ExecutionPlan?> RequestPlanAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _aiClient
                .GetDecisionAsync(
                    prompt,
                    ProviderOptionsResolver.ForPlanner(_options),
                    PlanningSystemPrompt,
                    imageBase64Png: null,
                    cancellationToken)
                .ConfigureAwait(false);

            var parseResult = _parser.Parse(raw);
            return parseResult.Success ? parseResult.Plan : null;
        }
        catch (AiClientException)
        {
            return null;
        }
    }
}
