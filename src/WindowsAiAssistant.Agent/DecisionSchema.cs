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
        "press_shortcut",
        "click_element",
        "focus_element",
        "read_element",
        "set_value",
        "select_element",
        "expand_collapse",
        "invoke_toggle",
        "scroll",
        "focus_window",
        "window_state",
        "move_window",
        "list_windows",
        "launch",
        "mouse_click",
        "mouse_scroll",
        "mouse_drag",
        "shell"
    };

    public const string JsonSchemaExample = """
        {
          "decisionType": "execute_action",
          "action": "open_app",
          "reason": "Short internal reason (English)",
          "target": "primary argument: app name / elementId / window title / url / exe or command / key",
          "parameters": {
            "message": "Turkish text, ONLY for respond/ask_user; other actions use their own keys"
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
