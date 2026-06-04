using System.Text;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent;

public sealed class PromptBuilder
{
    private readonly AgentOptions _options;

    public PromptBuilder(AgentOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Build(string userGoal, DesktopObservation observation, IEnumerable<AgentStep> priorSteps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userGoal);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(priorSteps);

        var builder = new StringBuilder();
        builder.AppendLine("You must reply with ONLY one JSON object. No markdown, no code fences, no extra text.");
        builder.AppendLine("Use the field name decisionType (never use type).");
        builder.AppendLine();
        builder.AppendLine("Allowed decisionType values: execute_action, ask_user, complete, stop");
        builder.AppendLine("Allowed action values:");
        builder.AppendLine("  respond, ask_user, stop, wait");
        builder.AppendLine("  open_app, open_url, type_text, press_key, press_shortcut");
        builder.AppendLine("Required shape:");
        builder.AppendLine(DecisionSchema.JsonSchemaExample.Trim());
        builder.AppendLine();
        builder.AppendLine("Multi-step rules:");
        builder.AppendLine("- For tasks needing multiple desktop actions, run them one per step (open_app, then type_text, etc.).");
        builder.AppendLine("- Do NOT use respond until all required physical actions succeeded.");
        builder.AppendLine("- After actions, use decisionType complete or respond with isComplete true.");
        builder.AppendLine("- Use wait only if the UI needs time before the next action.");
        builder.AppendLine();
        builder.AppendLine("Action rules:");
        builder.AppendLine("- respond / ask_user: parameters.message required (final user-facing text)");
        builder.AppendLine("- open_app: target=notepad|calc|mspaint|explorer");
        builder.AppendLine("- open_url: target=https://...");
        builder.AppendLine("- type_text: parameters.text");
        builder.AppendLine("- press_shortcut: target=Ctrl+A");
        builder.AppendLine();
        builder.AppendLine("Current desktop observation:");
        builder.AppendLine(observation.ToPromptSummary());
        builder.AppendLine();
        AppendStepHistory(builder, priorSteps);
        builder.AppendLine($"User goal: {userGoal.Trim()}");

        return builder.ToString();
    }

    private void AppendStepHistory(StringBuilder builder, IEnumerable<AgentStep> priorSteps)
    {
        var steps = priorSteps
            .OrderBy(step => step.Index)
            .TakeLast(Math.Clamp(_options.MaxPriorStepsInPrompt, 1, 20))
            .ToList();

        if (steps.Count == 0)
        {
            return;
        }

        builder.AppendLine("Prior steps this run:");
        foreach (var step in steps)
        {
            var action = step.ParsedDecision?.Action ?? "?";
            var target = step.ParsedDecision?.Target;
            var success = step.ActionResult?.Success == true ? "ok" : "fail";
            var resultMessage = step.ActionResult?.Message ?? "(no result)";
            var targetPart = string.IsNullOrWhiteSpace(target) ? string.Empty : $" target={target}";
            builder.AppendLine(
                $"  - step {step.Index + 1}: {action}{targetPart} -> {success}: {resultMessage}");
        }

        builder.AppendLine();
    }
}
