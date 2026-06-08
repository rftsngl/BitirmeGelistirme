using System.Text;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Agent.Planning;

internal static class PlanAwareObservationHints
{
    internal static void Append(StringBuilder builder, ExecutionPlan? plan, DesktopObservation observation, int currentPlanStepIndex)
    {
        if (plan is null)
        {
            return;
        }

        builder.AppendLine("PLAN-AWARE OBSERVATION (computed — trust over stale assumptions):");
        AppendWindowReuseHints(builder, plan, observation);
        AppendOfficeHints(builder, plan, observation);
        AppendCurrentStepHint(builder, plan, currentPlanStepIndex, observation);
        builder.AppendLine();
    }

    private static void AppendWindowReuseHints(StringBuilder builder, ExecutionPlan plan, DesktopObservation observation)
    {
        var mentionsOpen = plan.Steps.Any(step =>
            step.PreferredActions.Any(action => action.Contains("open_app", StringComparison.OrdinalIgnoreCase)) ||
            step.Intent.Contains("ac", StringComparison.OrdinalIgnoreCase));

        if (!mentionsOpen)
        {
            return;
        }

        var processes = observation.Windows
            .Select(window => window.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (processes.Count > 0)
        {
            builder.AppendLine($"- visibleWindows lists: {string.Join(", ", processes)} — prefer focus_window before open_app.");
        }
        else
        {
            builder.AppendLine("- visibleWindows empty for target apps — open_app may be appropriate.");
        }
    }

    private static void AppendOfficeHints(StringBuilder builder, ExecutionPlan plan, DesktopObservation observation)
    {
        if (!plan.Domain.Contains("office", StringComparison.OrdinalIgnoreCase) &&
            !plan.Skill.Contains("word", StringComparison.OrdinalIgnoreCase) &&
            !plan.Summary.Contains("word", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var wordWindows = observation.Windows
            .Where(window => window.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (wordWindows.Count > 0)
        {
            var sample = wordWindows[0];
            builder.AppendLine($"- WINWORD already open ({wordWindows.Count} window(s)) — use focus_window target={sample.WindowId}; skip open_app.");
        }
        else
        {
            builder.AppendLine("- WINWORD not in visibleWindows — open_app or launch may be needed.");
        }
    }

    private static void AppendCurrentStepHint(
        StringBuilder builder,
        ExecutionPlan plan,
        int currentPlanStepIndex,
        DesktopObservation observation)
    {
        if (plan.Steps.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(currentPlanStepIndex, 0, plan.Steps.Count - 1);
        var step = plan.Steps.OrderBy(s => s.Order).ElementAt(index);
        builder.AppendLine($"- active plan step {index + 1}/{plan.Steps.Count}: {step.Intent}");
        if (!string.IsNullOrWhiteSpace(step.SuccessCheck))
        {
            builder.AppendLine($"  verify next: {step.SuccessCheck}");
        }

        if (observation.ActiveProcessName.Equals("WindowsAiAssistant", StringComparison.OrdinalIgnoreCase) &&
            step.PreferredActions.Any(action => action.Contains("type_text", StringComparison.OrdinalIgnoreCase) ||
                                                action.Contains("click_element", StringComparison.OrdinalIgnoreCase)))
        {
            builder.AppendLine("- focus is on assistant UI — call focus_window to target app before UI actions.");
        }
    }
}
