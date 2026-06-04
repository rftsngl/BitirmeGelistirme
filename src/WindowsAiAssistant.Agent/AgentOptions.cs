using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Agent;

public sealed class AgentOptions
{
    public int MaxSteps { get; set; } = 5;
    public int MaxPriorStepsInPrompt { get; set; } = 5;
    public string SystemPrompt { get; set; } =
        "You are a helpful Windows desktop assistant.";
    public ProviderOptions Model { get; set; } = new();
}
