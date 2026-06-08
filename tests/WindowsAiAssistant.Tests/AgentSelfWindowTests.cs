using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Tests;

public sealed class AgentSelfWindowTests
{
    [Theory]
    [InlineData("WindowsAiAssistant.App")]
    [InlineData("WindowsAiAssistant")]
    public void IsAssistantProcess_AssistantNames_ReturnsTrue(string processName)
    {
        Assert.True(AgentSelfWindow.IsAssistantProcess(processName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("notepad")]
    [InlineData("chrome")]
    public void IsAssistantProcess_OtherProcesses_ReturnsFalse(string? processName)
    {
        Assert.False(AgentSelfWindow.IsAssistantProcess(processName));
    }
}
