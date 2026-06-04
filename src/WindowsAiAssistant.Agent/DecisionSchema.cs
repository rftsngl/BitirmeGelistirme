using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Agent;

public enum AgentDecisionType
{
    ExecuteAction,
    AskUser,
    Complete,
    Stop
}

public static class DecisionSchema
{
    public static readonly IReadOnlySet<string> SupportedDecisionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "execute_action",
        "ask_user",
        "complete",
        "stop"
    };

    public static readonly IReadOnlySet<string> SupportedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "respond",
        "ask_user",
        "stop",
        "wait",
        "open_app",
        "open_url",
        "type_text",
        "press_key",
        "press_shortcut"
    };

    public const string JsonSchemaExample = """
        {
          "decisionType": "execute_action",
          "action": "respond",
          "reason": "Short reason",
          "parameters": {
            "message": "Response text"
          },
          "isComplete": false
        }
        """;
}

public sealed class AgentDecision
{
    public required AgentDecisionType DecisionType { get; init; }
    public required string Action { get; init; }
    public string? Reason { get; init; }
    public string? Target { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool IsComplete { get; init; }

    public AgentAction ToAgentAction() =>
        new()
        {
            Action = Action,
            Target = Target,
            Parameters = Parameters
        };

    public string ToDisplayMessage()
    {
        if (Parameters.TryGetValue("message", out var message) && !string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        if (!string.IsNullOrWhiteSpace(Reason))
        {
            return Reason;
        }

        return DecisionType switch
        {
            AgentDecisionType.Complete => "Gorev tamamlandi.",
            AgentDecisionType.Stop => "Dongu durduruldu.",
            AgentDecisionType.AskUser => "Kullanicidan bilgi gerekiyor.",
            _ => $"Karar: {Action}"
        };
    }
}

public sealed class DecisionParseResult
{
    public bool Success { get; init; }
    public AgentDecision? Decision { get; init; }
    public string? ErrorMessage { get; init; }

    public static DecisionParseResult Ok(AgentDecision decision) =>
        new() { Success = true, Decision = decision };

    public static DecisionParseResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
