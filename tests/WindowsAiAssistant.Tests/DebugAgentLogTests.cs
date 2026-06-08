using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Debugging;

namespace WindowsAiAssistant.Tests;

public sealed class DebugAgentLogTests
{
    [Fact]
    public void Configure_ExplicitDisable_DisablesLogging()
    {
        DebugAgentLog.Configure(new RuntimeOptions
        {
            Logging = new LoggingOptions { EnableDebugAgentLog = false }
        });

        Assert.False(DebugAgentLog.IsEnabled);
    }

    [Fact]
    public void Configure_ExplicitEnable_EnablesLogging()
    {
        DebugAgentLog.Configure(new RuntimeOptions
        {
            Logging = new LoggingOptions { EnableDebugAgentLog = true }
        });

        Assert.True(DebugAgentLog.IsEnabled);
    }
}
