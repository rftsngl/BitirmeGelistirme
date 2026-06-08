namespace WindowsAiAssistant.Agent.Planning;

public static class PlanProgressTracker
{
    internal static ExecutionPlanStep? GetCurrentStep(AgentSession session)
    {
        if (session.ExecutionPlan is null || session.ExecutionPlan.Steps.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(session.CurrentPlanStepIndex, 0, session.ExecutionPlan.Steps.Count - 1);
        return session.ExecutionPlan.Steps.OrderBy(step => step.Order).ElementAt(index);
    }

    internal static void RecordStepOutcome(AgentSession session, bool success)
    {
        if (!success)
        {
            session.ConsecutiveStepFailures++;
            return;
        }

        session.ConsecutiveStepFailures = 0;
        if (session.ExecutionPlan is null)
        {
            return;
        }

        if (session.CurrentPlanStepIndex < session.ExecutionPlan.Steps.Count - 1)
        {
            session.CurrentPlanStepIndex++;
        }
    }

    public static string FormatProgressLine(AgentSession session)
    {
        if (session.ExecutionPlan is null || session.ExecutionPlan.Steps.Count == 0)
        {
            return string.Empty;
        }

        var current = session.CurrentPlanStepIndex + 1;
        var total = session.ExecutionPlan.Steps.Count;
        var step = GetCurrentStep(session);
        var intent = step?.Intent ?? session.ExecutionPlan.Summary;
        return $"Plan {current}/{total}: {intent}";
    }

    public static string? GetPlanHeadline(AgentSession session) =>
        string.IsNullOrWhiteSpace(session.ExecutionPlan?.Summary)
            ? null
            : session.ExecutionPlan.Summary.Trim();

    public static IReadOnlyList<PlanStepDisplayLine> BuildStepLines(AgentSession session)
    {
        if (session.ExecutionPlan is null || session.ExecutionPlan.Steps.Count == 0)
        {
            return [];
        }

        return session.ExecutionPlan.Steps
            .OrderBy(step => step.Order)
            .Select(step =>
            {
                var index = step.Order - 1;
                var marker = index == session.CurrentPlanStepIndex
                    ? PlanStepMarker.Active
                    : index < session.CurrentPlanStepIndex
                        ? PlanStepMarker.Completed
                        : PlanStepMarker.Pending;
                return new PlanStepDisplayLine
                {
                    Order = step.Order,
                    Intent = step.Intent,
                    Marker = marker
                };
            })
            .ToList();
    }

    public static string FormatSummaryCard(AgentSession session)
    {
        if (session.ExecutionPlan is null)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        var headline = GetPlanHeadline(session);
        if (!string.IsNullOrWhiteSpace(headline))
        {
            lines.Add(headline);
        }

        foreach (var step in BuildStepLines(session))
        {
            var marker = step.Marker switch
            {
                PlanStepMarker.Active => "▶",
                PlanStepMarker.Completed => "✓",
                _ => "○"
            };
            lines.Add($"{marker} {step.Order}. {step.Intent}");
        }

        if (session.PlanRevisionCount > 0)
        {
            lines.Add($"Plan revizyonu: v{session.PlanRevisionCount + 1}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
