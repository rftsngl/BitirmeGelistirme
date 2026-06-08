using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Planning;

public sealed class CompletionVerifierService
{
    private const string VerifierSystemPrompt =
        "You verify whether a Windows desktop automation goal is truly complete. " +
        "Reply with ONLY JSON: {\"complete\":true|false,\"missing\":\"Turkish explanation if incomplete\",\"suggestedNext\":\"optional action hint\"}.";

    private readonly AgentOptions _options;
    private readonly AiClient _aiClient;

    public CompletionVerifierService(AgentOptions options, AiClient aiClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
    }

    public async Task<CompletionVerificationResult> VerifyAsync(
        string userGoal,
        ExecutionPlan? plan,
        DesktopObservation observation,
        string proposedMessage,
        CancellationToken cancellationToken = default)
    {
        if (!_options.CompletionVerificationEnabled || PlanningEligibility.LooksDesktopGoal(userGoal) == false)
        {
            return CompletionVerificationResult.Complete();
        }

        var prompt = BuildPrompt(userGoal, plan, observation, proposedMessage);
        try
        {
            var raw = await _aiClient
                .GetDecisionAsync(
                    prompt,
                    ProviderOptionsResolver.ForVerifier(_options),
                    VerifierSystemPrompt,
                    imageBase64Png: null,
                    cancellationToken)
                .ConfigureAwait(false);

            return Parse(raw);
        }
        catch (AiClientException)
        {
            return CompletionVerificationResult.Complete();
        }
    }

    private static string BuildPrompt(
        string userGoal,
        ExecutionPlan? plan,
        DesktopObservation observation,
        string proposedMessage)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Verify goal completion before the agent tells the user the task is done.");
        builder.AppendLine($"User goal: {userGoal.Trim()}");
        builder.AppendLine($"Proposed user message: {proposedMessage.Trim()}");
        if (plan is not null)
        {
            builder.AppendLine(PlanningPromptBuilder.ToPromptSection(plan));
        }

        builder.AppendLine("Current observation:");
        builder.AppendLine(observation.ToPromptSummary());
        builder.AppendLine();
        builder.AppendLine("If ANY part of the user goal is not verifiably done, set complete=false and explain missing work in Turkish.");
        return builder.ToString();
    }

    private static CompletionVerificationResult Parse(string raw)
    {
        var json = DecisionJsonNormalizer.ExtractJsonObject(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            return CompletionVerificationResult.Complete();
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("complete", out var completeElement) &&
                completeElement.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                return CompletionVerificationResult.Complete();
            }

            var missing = root.TryGetProperty("missing", out var missingElement) &&
                          missingElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? missingElement.GetString()
                : "Hedef tam olarak dogrulanamadi.";
            var suggested = root.TryGetProperty("suggestedNext", out var suggestedElement) &&
                            suggestedElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? suggestedElement.GetString()
                : null;

            return CompletionVerificationResult.Incomplete(missing ?? "Eksik is var.", suggested);
        }
        catch
        {
            return CompletionVerificationResult.Complete();
        }
    }
}
