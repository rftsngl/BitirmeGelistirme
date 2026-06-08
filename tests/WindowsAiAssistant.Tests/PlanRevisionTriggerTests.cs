using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Agent.Planning;

namespace WindowsAiAssistant.Tests;

public sealed class PlanRevisionTriggerTests
{
    [Fact]
    public void ShouldRevise_ConsecutiveFailures_ReturnsTrue()
    {
        var options = new AgentOptions { PlanRevisionEnabled = true };
        var session = new AgentSession
        {
            RunId = "r1",
            UserGoal = "Word ac ve yaz",
            ExecutionPlan = new ExecutionPlan
            {
                Steps = [new ExecutionPlanStep { Order = 1, Intent = "ac" }]
            },
            ConsecutiveStepFailures = 2
        };

        var shouldRevise = PlanRevisionTrigger.ShouldRevise(options, session, stepIndex: 2, maxSteps: 10, lastStepFailed: true);
        Assert.True(shouldRevise);
    }

    [Fact]
    public void ShouldRevise_Disabled_ReturnsFalse()
    {
        var options = new AgentOptions { PlanRevisionEnabled = false };
        var session = new AgentSession
        {
            RunId = "r1",
            UserGoal = "Word ac",
            ExecutionPlan = new ExecutionPlan { Steps = [new ExecutionPlanStep { Order = 1, Intent = "ac" }] },
            ConsecutiveStepFailures = 3
        };

        Assert.False(PlanRevisionTrigger.ShouldRevise(options, session, 2, 10, true));
    }
}
