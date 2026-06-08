using WindowsAiAssistant.Runtime.Session;

namespace WindowsAiAssistant.Tests;

public sealed class AgentRunScopeTests
{
    [Fact]
    public void Current_IsNullOutsideScope()
    {
        Assert.Null(AgentRunScope.Current);
    }

    [Fact]
    public void Dispose_ClearsCurrentScope()
    {
        using (var scope = new AgentRunScope("run-a", "goal-a"))
        {
            Assert.Same(scope, AgentRunScope.Current);
            Assert.Equal("run-a", scope.RunId);
        }

        Assert.Null(AgentRunScope.Current);
    }

    [Fact]
    public void Constructor_WithTriggerSource_StoresMetadata()
    {
        using var scope = new AgentRunScope("run-b", "goal-b", "voice_overlay");

        Assert.Equal("voice_overlay", scope.TriggerSource);
        Assert.True(scope.StartedAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void NestedScopes_RestoreOuterOnDispose()
    {
        using var outer = new AgentRunScope("outer", "goal-outer");
        Assert.Same(outer, AgentRunScope.Current);

        using (var inner = new AgentRunScope("inner", "goal-inner"))
        {
            Assert.Same(inner, AgentRunScope.Current);
        }

        Assert.Same(outer, AgentRunScope.Current);
    }
}
