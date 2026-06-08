using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Agent.Planning;

namespace WindowsAiAssistant.Tests;

public sealed class PlanProgressTrackerTests
{
    [Fact]
    public void RecordStepOutcome_Success_AdvancesPlanIndex()
    {
        var session = new AgentSession
        {
            RunId = "r1",
            UserGoal = "test",
            ExecutionPlan = new ExecutionPlan
            {
                Steps =
                [
                    new ExecutionPlanStep { Order = 1, Intent = "bir" },
                    new ExecutionPlanStep { Order = 2, Intent = "iki" }
                ]
            }
        };

        PlanProgressTracker.RecordStepOutcome(session, success: true);
        Assert.Equal(1, session.CurrentPlanStepIndex);
    }

    [Fact]
    public void FormatSummaryCard_ShowsProgressMarkers()
    {
        var session = new AgentSession
        {
            RunId = "r1",
            UserGoal = "test",
            CurrentPlanStepIndex = 1,
            ExecutionPlan = new ExecutionPlan
            {
                Summary = "Word is akisi",
                Steps =
                [
                    new ExecutionPlanStep { Order = 1, Intent = "Word ac" },
                    new ExecutionPlanStep { Order = 2, Intent = "Baslik yaz" }
                ]
            }
        };

        var card = PlanProgressTracker.FormatSummaryCard(session);
        Assert.Contains("Word is akisi", card, StringComparison.Ordinal);
        Assert.Contains("▶", card, StringComparison.Ordinal);
    }
}
