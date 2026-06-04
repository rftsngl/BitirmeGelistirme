using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Agent;

public sealed class AgentStep
{
    public int Index { get; init; }
    public string? LlmRawOutput { get; init; }
    public AgentDecision? ParsedDecision { get; init; }
    public ActionResult? ActionResult { get; init; }
}
