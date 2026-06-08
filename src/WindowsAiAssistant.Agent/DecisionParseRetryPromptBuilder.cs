using System.Text;
using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Agent;

internal static class DecisionParseRetryPromptBuilder
{
    internal static string Build(
        string originalPrompt,
        DecisionParseResult failed,
        IReadOnlyList<UiElementSnapshot>? uiElements,
        string? userGoal = null)
    {
        var builder = new StringBuilder(originalPrompt);
        builder.AppendLine();
        builder.AppendLine($"Your previous reply was invalid: {failed.ErrorMessage}");
        StrategyRecoveryPromptBuilder.AppendParseRetryStrategy(builder, failed, userGoal, uiElements);

        switch (failed.ErrorCode)
        {
            case DecisionParseErrorCode.InvalidElementId:
                AppendElementIdGuidance(builder, uiElements);
                break;
            case DecisionParseErrorCode.InvalidJson:
            case DecisionParseErrorCode.MissingJson:
                builder.AppendLine("Return ONLY raw JSON — no markdown fences, no commentary.");
                builder.AppendLine("Schema example:");
                builder.AppendLine(DecisionSchema.JsonSchemaExample.Trim());
                break;
            case DecisionParseErrorCode.MissingDecisionType:
            case DecisionParseErrorCode.LegacyTypeField:
                builder.AppendLine("Use decisionType (execute_action | ask_user | complete | stop). Never use legacy field 'type'.");
                break;
            case DecisionParseErrorCode.MissingRequiredField:
                builder.AppendLine("Include all required fields for the chosen action (target, parameters.message, parameters.elementId, etc.).");
                break;
            case DecisionParseErrorCode.TypeActionMismatch:
                builder.AppendLine("Match decisionType to action (ask_user→ask_user, complete→respond|stop).");
                break;
        }

        builder.AppendLine("Return ONLY one valid JSON object matching the schema.");
        return builder.ToString();
    }

    private static void AppendElementIdGuidance(StringBuilder builder, IReadOnlyList<UiElementSnapshot>? uiElements)
    {
        builder.AppendLine("For UI actions, target MUST be a valid elementId from uiElements (e.g. btn-save-a1b2).");
        builder.AppendLine("Do NOT use visible button labels, chat badges, or overlay text as target.");

        if (uiElements is null || uiElements.Count == 0)
        {
            builder.AppendLine("uiElements is empty — use list_windows, shell, P1 integrations, or respond from visibleWindows.");
            builder.AppendLine("Do NOT use click_element until valid elementIds exist.");
            return;
        }

        var samples = uiElements
            .Select(element => element.ElementId)
            .Where(UiElementIdValidator.IsValidFormat)
            .Take(8)
            .ToList();

        if (samples.Count > 0)
        {
            builder.AppendLine($"Available elementIds: {string.Join(", ", samples)}");
        }
    }
}
