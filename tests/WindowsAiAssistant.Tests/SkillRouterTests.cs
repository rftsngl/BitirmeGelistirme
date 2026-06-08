using WindowsAiAssistant.Agent.Planning;
using WindowsAiAssistant.Agent.Skills;
using WindowsAiAssistant.Runtime.Observation;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Tests;

public sealed class SkillRouterTests
{
    [Fact]
    public void ResolveDomain_WordGoal_ReturnsOffice()
    {
        var router = new SkillRouter();
        var domain = router.ResolveDomain(
            "Word ac ve baslik yaz",
            new DesktopObservation(),
            new ExecutionPlan { Domain = "office" });

        Assert.Equal(WorkflowSkillDomain.Office, domain);
    }

    [Fact]
    public void ResolveDomain_MuteGoal_ReturnsIntegration()
    {
        var router = new SkillRouter();
        var domain = router.ResolveDomain("sesi kapat", new DesktopObservation(), plan: null);
        Assert.Equal(WorkflowSkillDomain.Integration, domain);
    }
}
