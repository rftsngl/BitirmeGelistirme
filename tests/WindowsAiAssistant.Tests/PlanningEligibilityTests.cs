using WindowsAiAssistant.Agent.Planning;

namespace WindowsAiAssistant.Tests;

public sealed class PlanningEligibilityTests
{
    [Fact]
    public void ShouldPlan_MultiStepWordGoal_ReturnsTrue()
    {
        var eligible = PlanningEligibility.ShouldPlan(
            "Word aç. sonra boş belgeyi seç. Keloğlan masalları başlığı oluştur.");

        Assert.True(eligible);
    }

    [Fact]
    public void ShouldPlan_Greeting_ReturnsFalse()
    {
        var eligible = PlanningEligibility.ShouldPlan("merhaba nasılsın");
        Assert.False(eligible);
    }

    [Fact]
    public void ShouldPlan_MuteGoal_ReturnsFalse()
    {
        var eligible = PlanningEligibility.ShouldPlan("sesi kapat");
        Assert.False(eligible);
    }
}
