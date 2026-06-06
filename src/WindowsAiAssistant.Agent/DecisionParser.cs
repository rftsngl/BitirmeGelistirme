using System.Text.Json;
using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Agent;

public sealed class DecisionParser
{
    public DecisionParseResult Parse(string llmRawOutput)
    {
        if (string.IsNullOrWhiteSpace(llmRawOutput))
        {
            return DecisionParseResult.Fail("Asistan cevabi bos geldi. Lutfen tekrar deneyin.");
        }

        var jsonText = DecisionJsonNormalizer.ExtractJsonObject(llmRawOutput);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return DecisionParseResult.Fail("Asistan gecerli bir JSON karari dondurmedi.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonText);
        }
        catch (JsonException)
        {
            return DecisionParseResult.Fail("Karar JSON formatinda degil. Lutfen tekrar deneyin.");
        }

        using (document)
        {
            return ParseRoot(document.RootElement);
        }
    }

    private static DecisionParseResult ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return DecisionParseResult.Fail("Karar tek bir JSON nesnesi olmali.");
        }

        if (!TryReadDecisionType(root, out var decisionType, out var typeError))
        {
            return DecisionParseResult.Fail(typeError!);
        }

        var action = ReadString(root, "action");
        if (string.IsNullOrWhiteSpace(action))
        {
            return DecisionParseResult.Fail("Kararda 'action' alani eksik.");
        }

        action = action.Trim();

        if (!DecisionSchema.SupportedActions.Contains(action))
        {
            return DecisionParseResult.Fail($"Desteklenmeyen islem: '{action}'.");
        }

        var typeActionError = ValidateDecisionTypeAndAction(decisionType, action);
        if (typeActionError is not null)
        {
            return DecisionParseResult.Fail(typeActionError);
        }

        var parameters = ReadParameters(root);
        var target = ReadString(root, "target");
        var parameterError = ValidateRequiredParameters(action, parameters, target);
        if (parameterError is not null)
        {
            return DecisionParseResult.Fail(parameterError);
        }

        var uiElementError = ValidateUiElementTarget(action, parameters, target);
        if (uiElementError is not null)
        {
            return DecisionParseResult.Fail(uiElementError);
        }

        var decision = new AgentDecision
        {
            DecisionType = decisionType,
            Action = action,
            Reason = ReadString(root, "reason"),
            Target = target,
            Parameters = parameters,
            IsComplete = ReadBool(root, "isComplete")
        };

        return DecisionParseResult.Ok(decision);
    }

    private static bool TryReadDecisionType(
        JsonElement root,
        out AgentDecisionType decisionType,
        out string? error)
    {
        decisionType = default;
        error = null;

        var rawType = ReadString(root, "decisionType");
        if (string.IsNullOrWhiteSpace(rawType))
        {
            if (root.TryGetProperty("type", out _))
            {
                error = "Eski 'type' alani kullanilmis. Lutfen 'decisionType' kullanin.";
                return false;
            }

            error = "Kararda 'decisionType' alani eksik.";
            return false;
        }

        rawType = rawType.Trim();
        if (!DecisionSchema.SupportedDecisionTypes.Contains(rawType))
        {
            error = $"Desteklenmeyen karar tipi: '{rawType}'.";
            return false;
        }

        decisionType = rawType.ToLowerInvariant() switch
        {
            "execute_action" => AgentDecisionType.ExecuteAction,
            "ask_user" => AgentDecisionType.AskUser,
            "complete" => AgentDecisionType.Complete,
            "stop" => AgentDecisionType.Stop,
            _ => default
        };

        return true;
    }

    private static string? ValidateDecisionTypeAndAction(AgentDecisionType decisionType, string action)
    {
        return decisionType switch
        {
            AgentDecisionType.AskUser when !action.Equals("ask_user", StringComparison.OrdinalIgnoreCase) =>
                "ask_user karar tipi icin action 'ask_user' olmali.",
            AgentDecisionType.Stop when !action.Equals("stop", StringComparison.OrdinalIgnoreCase) =>
                "stop karar tipi icin action 'stop' olmali.",
            AgentDecisionType.Complete when !action.Equals("stop", StringComparison.OrdinalIgnoreCase) &&
                !action.Equals("respond", StringComparison.OrdinalIgnoreCase) =>
                "complete karar tipi icin action 'stop' veya 'respond' olmali.",
            _ => null
        };
    }

    private static string? ValidateRequiredParameters(
        string action,
        IReadOnlyDictionary<string, string> parameters,
        string? target)
    {
        return action.ToLowerInvariant() switch
        {
            "respond" or "ask_user" when !parameters.ContainsKey("message") ||
                string.IsNullOrWhiteSpace(parameters["message"]) =>
                $"'{action}' islemi icin parameters.message zorunludur.",
            "open_app" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(FirstParameter(parameters,
                    "app", "name", "application", "executable", "program", "target")) =>
                "open_app icin target veya parameters.app gerekli.",
            "open_url" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "url")) =>
                "open_url icin target veya parameters.url gerekli.",
            "type_text" when string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "text")) =>
                "type_text icin parameters.text gerekli.",
            "press_key" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "key")) =>
                "press_key icin target veya parameters.key gerekli.",
            "press_shortcut" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "shortcut")) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "keys")) =>
                "press_shortcut icin target veya parameters.shortcut gerekli.",
            "click_element" or "focus_element" or "read_element" or
            "select_element" or "expand_collapse" or "invoke_toggle" or "scroll" when
                string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "elementId")) =>
                $"'{action}' icin target veya parameters.elementId gerekli.",
            "set_value" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "elementId")) =>
                "set_value icin target veya parameters.elementId gerekli.",
            "set_value" when string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "value")) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "text")) =>
                "set_value icin parameters.value gerekli.",
            "focus_window" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "windowId")) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "title")) =>
                "focus_window icin target veya parameters.windowId/title gerekli.",
            "window_state" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "windowId")) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "title")) =>
                "window_state icin target veya parameters.windowId/title gerekli.",
            "window_state" when string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "state")) =>
                "window_state icin parameters.state gerekli (minimize|maximize|restore|close).",
            "launch" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(FirstParameter(parameters,
                    "app", "command", "uri", "name", "path",
                    "application", "executable", "program", "file", "target")) =>
                "launch icin target veya parameters.app/command/uri gerekli.",
            "mouse_click" when string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "elementId")) &&
                (string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "x")) ||
                 string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "y"))) =>
                "mouse_click icin parameters.elementId veya parameters.x + parameters.y gerekli.",
            "move_window" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "windowId")) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "title")) =>
                "move_window icin target veya parameters.windowId/title gerekli.",
            "mouse_drag" when
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "startX")) ||
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "startY")) ||
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "endX")) ||
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "endY")) =>
                "mouse_drag icin parameters.startX, startY, endX, endY gerekli.",
            "shell" when string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "command")) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "cmd")) &&
                string.IsNullOrWhiteSpace(ReadStringFromParameters(parameters, "script")) =>
                "shell icin target veya parameters.command gerekli.",
            _ => null
        };
    }

    private static string? ValidateUiElementTarget(
        string action,
        IReadOnlyDictionary<string, string> parameters,
        string? target)
    {
        if (!UiElementIdValidator.IsUiAutomationAction(action) &&
            !action.Equals("mouse_click", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var elementId = UiElementIdValidator.GetElementTarget(action, target, parameters);
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        if (UiElementIdValidator.IsValidFormat(elementId))
        {
            return null;
        }

        return $"'{action}' icin target gecerli bir elementId olmali (uiElements listesinden; ornek: btn-kaydet-a1b2). " +
               "Gorunur metin, onay rozeti veya sohbet etiketi kullanmayin.";
    }

    private static string? ReadStringFromParameters(
        IReadOnlyDictionary<string, string> parameters,
        string key) =>
        parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? FirstParameter(
        IReadOnlyDictionary<string, string> parameters,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadStringFromParameters(parameters, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }


    private static IReadOnlyDictionary<string, string> ReadParameters(JsonElement root)
    {
        if (!root.TryGetProperty("parameters", out var parametersElement) ||
            parametersElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in parametersElement.EnumerateObject())
        {
            map[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => property.Value.GetRawText()
            };
        }

        return map;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static bool ReadBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            _ => false
        };
    }
}

internal static class DecisionJsonNormalizer
{
    public static string ExtractJsonObject(string raw)
    {
        var trimmed = StripMarkdownFences(raw.Trim());
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return string.Empty;
    }

    private static string StripMarkdownFences(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var lines = value.Split('\n');
        if (lines.Length < 2)
        {
            return value;
        }

        var startIndex = 1;
        var endIndex = lines.Length - 1;
        if (lines[^1].Trim().Equals("```", StringComparison.Ordinal))
        {
            endIndex = lines.Length - 2;
        }

        if (endIndex < startIndex)
        {
            return value;
        }

        return string.Join('\n', lines[startIndex..(endIndex + 1)]).Trim();
    }
}
