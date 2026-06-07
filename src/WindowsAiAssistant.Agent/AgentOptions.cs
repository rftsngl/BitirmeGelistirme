using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Agent;

public sealed class AgentOptions
{
    public int MaxSteps { get; set; } = 10;
    public int MaxPriorStepsInPrompt { get; set; } = 10;
    public string SystemPrompt { get; set; } =
        "You are an autonomous Windows desktop operator. TOOL PRIORITY: integrations first, then launch/open, " +
        "then shell, UI automation last (explicit in-app UI only). Never use UI clicks for system tasks the app " +
        "handles via dedicated actions. Conversation-only → respond/complete. Never automate the assistant window. " +
        "Strict JSON; Turkish user messages. Safety: avoid irreversible/destructive/privilege/secret exposure.";
    public ProviderOptions Model { get; set; } = new();
}
