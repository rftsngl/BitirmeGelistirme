using WindowsAiAssistant.Agent;

namespace WindowsAiAssistant.Tests;

public sealed class GoalRoutingHintsTests
{
    [Theory]
    [InlineData("sesi kapat", "mute")]
    [InlineData("sessize al", "mute")]
    [InlineData("sesi ac", "unmute")]
    [InlineData("sesi yükselt", "set_volume")]
    public void TryResolveAudioRoute_MatchesExpectedMode(string goal, string expectedMode)
    {
        var route = GoalRoutingHints.TryResolveAudioRoute(goal);

        Assert.NotNull(route);
        Assert.Equal(expectedMode, route!.Mode);
    }

    [Fact]
    public void TryResolveAudioRoute_ExplicitPercent_ReturnsLevel()
    {
        var route = GoalRoutingHints.TryResolveAudioRoute("ses seviyesini %45 yap");

        Assert.NotNull(route);
        Assert.Equal("set_volume", route!.Mode);
        Assert.Equal(45, route.Level);
    }

    [Fact]
    public void TryBuildFastAudioDecision_ReturnsAudioPowerAction()
    {
        var decision = GoalRoutingHints.TryBuildFastAudioDecision("sesi sustur");

        Assert.NotNull(decision);
        Assert.Equal("audio_power", decision!.Action);
        Assert.Equal("mute", decision.Parameters["mode"]);
    }

    [Fact]
    public void TryBuildFastNetworkDecision_ReturnsNetworkStatus()
    {
        var decision = GoalRoutingHints.TryBuildFastNetworkDecision("internet baglantisi var mi");

        Assert.NotNull(decision);
        Assert.Equal("network_status", decision!.Action);
        Assert.Equal("status", decision.Parameters["mode"]);
    }

    [Fact]
    public void TryBuildFastNetworkDecision_AdapterQuery_UsesAdaptersMode()
    {
        var decision = GoalRoutingHints.TryBuildFastNetworkDecision("internet baglantisi adaptor listesi");

        Assert.NotNull(decision);
        Assert.Equal("adapters", decision!.Parameters["mode"]);
    }

    [Fact]
    public void TryBuildFastPerfDecision_ReturnsPerfCounterSnapshot()
    {
        var decision = GoalRoutingHints.TryBuildFastPerfDecision("cpu kullanimi nedir");

        Assert.NotNull(decision);
        Assert.Equal("perf_counter", decision!.Action);
        Assert.Equal("snapshot", decision.Parameters["mode"]);
    }

    [Fact]
    public void TryBuildFastDecision_CompositeGoal_ReturnsNull()
    {
        var decision = GoalRoutingHints.TryBuildFastDecision("sesi kapat ve notepad ac");

        Assert.Null(decision);
    }

    [Fact]
    public void ShouldCompleteAfterFastRoute_MatchingAction_ReturnsTrue()
    {
        var shouldComplete = GoalRoutingHints.ShouldCompleteAfterFastRoute("sesi kapat", "audio_power");

        Assert.True(shouldComplete);
    }

    [Fact]
    public void ShouldCompleteAfterFastRoute_DifferentAction_ReturnsFalse()
    {
        var shouldComplete = GoalRoutingHints.ShouldCompleteAfterFastRoute("sesi kapat", "click_element");

        Assert.False(shouldComplete);
    }

    [Fact]
    public void TryBuildFastDecision_UnrelatedGoal_ReturnsNull()
    {
        var decision = GoalRoutingHints.TryBuildFastDecision("masaustunu temizle");

        Assert.Null(decision);
    }
}
