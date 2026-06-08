using WindowsAiAssistant.Agent;

namespace WindowsAiAssistant.Tests;

public sealed class AgentSessionFailureTrackingTests
{
    [Fact]
    public void RecordActionFailure_IncrementsSameKey()
    {
        var session = CreateSession();

        Assert.Equal(1, session.RecordActionFailure("click_element", "btn-save-a1b2"));
        Assert.Equal(2, session.RecordActionFailure("click_element", "btn-save-a1b2"));
    }

    [Fact]
    public void RecordActionFailure_DifferentTargetsAreSeparate()
    {
        var session = CreateSession();

        Assert.Equal(1, session.RecordActionFailure("click_element", "btn-a"));
        Assert.Equal(1, session.RecordActionFailure("click_element", "btn-b"));
    }

    [Fact]
    public void ResetActionFailure_ClearsCounter()
    {
        var session = CreateSession();
        session.RecordActionFailure("shell", "Get-Process");
        session.RecordActionFailure("shell", "Get-Process");

        session.ResetActionFailure("shell", "Get-Process");

        Assert.Equal(1, session.RecordActionFailure("shell", "Get-Process"));
    }

    [Fact]
    public void BuildFailureKey_IsCaseInsensitive()
    {
        var keyA = AgentSession.BuildFailureKey("Click_Element", "BTN-A");
        var keyB = AgentSession.BuildFailureKey("click_element", "btn-a");

        Assert.Equal(keyA, keyB);
    }

    private static AgentSession CreateSession() =>
        new()
        {
            RunId = "test-run",
            UserGoal = "test goal"
        };
}
