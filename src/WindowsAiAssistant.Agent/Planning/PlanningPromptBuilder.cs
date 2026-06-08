using System.Text;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Planning;

internal static class PlanningPromptBuilder
{
    internal static string Build(string userGoal, DesktopObservation observation)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are a Windows desktop task planner. The executor agent will follow your plan step by step.");
        builder.AppendLine("Reply with ONLY one JSON object. No markdown, no code fences, no extra text.");
        builder.AppendLine();
        builder.AppendLine("Required JSON shape:");
        builder.AppendLine("""
        {
          "goalType": "desktop_single | desktop_multi",
          "domain": "office | window | integration | generic_desktop",
          "skill": "optional skill id e.g. word_document",
          "summary": "Turkish one-line summary of the approach",
          "preconditions": ["check visibleWindows before open_app", "..."],
          "steps": [
            {
              "order": 1,
              "intent": "what this step achieves",
              "preferredActions": ["focus_window", "com_invoke", "press_shortcut Ctrl+N"],
              "successCheck": "how to verify this step succeeded from next observation"
            }
          ],
          "antiPatterns": ["do not open_app if app already in visibleWindows", "..."],
          "estimatedSteps": 4
        }
        """);
        builder.AppendLine();
        builder.AppendLine("Planning rules:");
        builder.AppendLine("- TOOL PRIORITY for preferredActions: P1 integrations → P2 open/launch → P2b COM/shortcut → P3 shell → P4 UI last.");
        builder.AppendLine("- BEFORE open_app: plan a visibleWindows check; reuse existing app windows with focus_window.");
        builder.AppendLine("- Multi-part user goals must become ordered steps; avoid redundant launches or duplicate documents.");
        builder.AppendLine("- Office: prefer com_invoke Documents.Add or Ctrl+N over clicking start-screen tiles.");
        builder.AppendLine("- Keep estimatedSteps realistic (usually 2-8 for desktop goals).");
        builder.AppendLine("- If the user goal is pure conversation with no desktop change, use goalType=conversation and steps=[].");
        builder.AppendLine();
        builder.AppendLine("Current desktop observation:");
        builder.AppendLine(observation.ToPromptSummary());
        builder.AppendLine();
        builder.AppendLine($"User goal: {userGoal.Trim()}");

        return builder.ToString();
    }

    internal static string BuildRevision(
        string userGoal,
        DesktopObservation observation,
        ExecutionPlan currentPlan,
        int currentPlanStepIndex,
        IEnumerable<AgentStep> priorSteps,
        string revisionReason)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Revise the execution plan. The previous plan is not working or the environment changed.");
        builder.AppendLine($"Revision reason: {revisionReason}");
        builder.AppendLine();
        builder.AppendLine("Previous plan:");
        builder.AppendLine(ToPromptSection(currentPlan, currentPlanStepIndex));
        builder.AppendLine();
        builder.AppendLine("Prior steps this run:");
        foreach (var step in priorSteps.OrderBy(item => item.Index))
        {
            var action = step.ParsedDecision?.Action ?? "?";
            var success = step.ActionResult?.Success == true ? "ok" : "fail";
            builder.AppendLine($"  - {action} -> {success}: {step.ActionResult?.Message}");
        }

        builder.AppendLine();
        builder.AppendLine("Reply with ONLY a NEW full plan JSON (same shape as initial planning).");
        builder.AppendLine("Focus on REMAINING work from the user goal; drop completed steps.");
        builder.AppendLine();
        builder.AppendLine("Current desktop observation:");
        builder.AppendLine(observation.ToPromptSummary());
        builder.AppendLine();
        builder.AppendLine($"User goal: {userGoal.Trim()}");
        return builder.ToString();
    }

    internal static string ToPromptSection(ExecutionPlan plan, int? currentPlanStepIndex = null)
    {
        var builder = new StringBuilder();
        var revisionLabel = currentPlanStepIndex is null ? "created at run start" : "active";
        builder.AppendLine($"EXECUTION PLAN ({revisionLabel} — follow unless observation proves it wrong):");
        builder.AppendLine($"- goalType: {plan.GoalType}");
        if (!string.IsNullOrWhiteSpace(plan.Domain))
        {
            builder.AppendLine($"- domain: {plan.Domain}");
        }

        if (!string.IsNullOrWhiteSpace(plan.Skill))
        {
            builder.AppendLine($"- skill: {plan.Skill}");
        }
        if (!string.IsNullOrWhiteSpace(plan.Summary))
        {
            builder.AppendLine($"- summary: {plan.Summary}");
        }

        builder.AppendLine($"- estimatedSteps: {plan.EstimatedSteps}");
        if (plan.Preconditions.Count > 0)
        {
            builder.AppendLine("- preconditions:");
            foreach (var item in plan.Preconditions)
            {
                builder.AppendLine($"  * {item}");
            }
        }

        if (currentPlanStepIndex is not null && plan.Steps.Count > 0)
        {
            var idx = Math.Clamp(currentPlanStepIndex.Value, 0, plan.Steps.Count - 1);
            var active = plan.Steps.OrderBy(s => s.Order).ElementAt(idx);
            builder.AppendLine($"- CURRENT plan step: {idx + 1}/{plan.Steps.Count} — {active.Intent}");
        }

        builder.AppendLine("- planned steps:");
        foreach (var step in plan.Steps.OrderBy(s => s.Order))
        {
            var actions = step.PreferredActions.Count > 0
                ? string.Join(", ", step.PreferredActions)
                : "(executor chooses)";
            var marker = currentPlanStepIndex is not null && step.Order - 1 == currentPlanStepIndex.Value ? ">>" : "  ";
            builder.AppendLine($"  {marker}{step.Order}. {step.Intent}");
            builder.AppendLine($"     preferred: {actions}");
            if (!string.IsNullOrWhiteSpace(step.SuccessCheck))
            {
                builder.AppendLine($"     verify: {step.SuccessCheck}");
            }
        }

        if (plan.AntiPatterns.Count > 0)
        {
            builder.AppendLine("- anti-patterns (NEVER do these):");
            foreach (var item in plan.AntiPatterns)
            {
                builder.AppendLine($"  * {item}");
            }
        }

        builder.AppendLine("- Advance the plan one step at a time; after each action read lastActionResult and observation before the next.");
        builder.AppendLine("- If observation contradicts the plan, adapt — but do not repeat failed patterns listed above.");
        builder.AppendLine();

        return builder.ToString().TrimEnd();
    }
}
