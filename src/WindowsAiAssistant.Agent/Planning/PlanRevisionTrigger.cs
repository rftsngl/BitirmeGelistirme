namespace WindowsAiAssistant.Agent.Planning;

internal static class PlanRevisionTrigger
{
    private const int MaxRevisionsPerRun = 2;

    internal static bool ShouldRevise(
        AgentOptions options,
        AgentSession session,
        int stepIndex,
        int maxSteps,
        bool lastStepFailed)
    {
        if (!options.PlanRevisionEnabled || session.ExecutionPlan is null)
        {
            return false;
        }

        if (session.PlanRevisionCount >= MaxRevisionsPerRun)
        {
            return false;
        }

        if (session.ConsecutiveStepFailures >= 2 && lastStepFailed)
        {
            return true;
        }

        var budgetThreshold = (int)Math.Ceiling(maxSteps * 0.7);
        if (stepIndex >= budgetThreshold &&
            session.CurrentPlanStepIndex < session.ExecutionPlan.Steps.Count - 1)
        {
            return true;
        }

        return false;
    }
}
