using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Agent.Planning;

namespace WindowsAiAssistant.App.Models;

public static class PlanUiSync
{
    public static void ApplyFromProgress(ConversationItem item, AgentStepProgress progress)
    {
        if (!string.IsNullOrWhiteSpace(progress.PlanSummary))
        {
            item.PlanSummary = progress.PlanSummary;
        }

        if (!string.IsNullOrWhiteSpace(progress.PlanProgressLine))
        {
            item.PlanProgressLine = progress.PlanProgressLine;
        }

        item.PlanHeadline = progress.PlanHeadline ?? string.Empty;
        item.SkillDomainLabel = SkillDomainFormatter.Format(progress.SkillDomain);
        item.PlanRevisionLabel = progress.PlanRevision is > 0
            ? $"Revizyon v{progress.PlanRevision}"
            : string.Empty;
        ReplacePlanSteps(item, progress.PlanSteps);
    }

    public static void ApplyFromSession(ConversationItem item, AgentSession session)
    {
        if (session.ExecutionPlan is null)
        {
            ClearPlan(item);
            return;
        }

        item.PlanSummary = PlanProgressTracker.FormatSummaryCard(session);
        item.PlanProgressLine = PlanProgressTracker.FormatProgressLine(session);
        item.PlanHeadline = PlanProgressTracker.GetPlanHeadline(session) ?? string.Empty;
        item.SkillDomainLabel = SkillDomainFormatter.Format(session.SkillDomain);
        item.PlanRevisionLabel = session.PlanRevisionCount > 0
            ? $"Revizyon v{session.PlanRevisionCount + 1}"
            : string.Empty;
        ReplacePlanSteps(item, PlanProgressTracker.BuildStepLines(session));
    }

    private static void ReplacePlanSteps(ConversationItem item, IReadOnlyList<PlanStepDisplayLine>? steps)
    {
        item.PlanSteps.Clear();
        if (steps is null)
        {
            item.NotifyPlanChanged();
            return;
        }

        foreach (var step in steps)
        {
            item.PlanSteps.Add(step);
        }

        item.NotifyPlanChanged();
    }

    private static void ClearPlan(ConversationItem item)
    {
        item.PlanSummary = string.Empty;
        item.PlanProgressLine = string.Empty;
        item.PlanHeadline = string.Empty;
        item.SkillDomainLabel = string.Empty;
        item.PlanRevisionLabel = string.Empty;
        item.PlanSteps.Clear();
        item.NotifyPlanChanged();
    }
}
