using System.Text.Json;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure;

/// <summary>
/// Parses the JSON-only next-action contract used by model decision providers.
/// Duplicated alongside OpenAI-compatible parsing to avoid refactoring the existing adapter.
/// </summary>
internal static class NextActionDecisionContractJsonParser
{
    public static bool TryParseDecision(
        string responseContent,
        out NextActionDecision nextActionDecision,
        out string? reason)
    {
        nextActionDecision = new NextActionDecision();
        reason = null;

        using var document = JsonDocument.Parse(responseContent);
        var root = document.RootElement;
        if (root.TryGetProperty("nextActionDecision", out var nestedDecision) &&
            nestedDecision.ValueKind == JsonValueKind.Object)
        {
            root = nestedDecision;
        }

        if (!TryGetEnum(root, "kind", out DecisionKind kind))
        {
            reason = "missing-kind";
            return false;
        }

        var message = TryGetString(root, "message");
        var rationale = TryGetString(root, "rationale");
        var confidence = TryGetDouble(root, "confidence");
        var requiresApprovalReason = TryGetString(root, "requiresApprovalReason");
        var retryReason = TryGetString(root, "retryReason");
        var stopReason = TryGetString(root, "stopReason");

        var executeAction = root.TryGetProperty("executeAction", out var executeActionElement)
            ? ParseExecuteAction(executeActionElement, out reason)
            : null;
        if (reason is not null)
        {
            return false;
        }

        var askObserve = root.TryGetProperty("askObserve", out var askObserveElement)
            ? ParseAskObserve(askObserveElement)
            : null;

        var askApproval = root.TryGetProperty("askApproval", out var askApprovalElement)
            ? ParseAskApproval(askApprovalElement, out reason)
            : null;
        if (reason is not null)
        {
            return false;
        }

        var retry = root.TryGetProperty("retry", out var retryElement)
            ? ParseRetry(retryElement, out reason)
            : null;
        if (reason is not null)
        {
            return false;
        }

        var stop = root.TryGetProperty("stop", out var stopElement)
            ? ParseStop(stopElement, out reason)
            : null;
        if (reason is not null)
        {
            return false;
        }

        if (kind == DecisionKind.ExecuteAction && executeAction is null)
        {
            reason = "missing-execute-action";
            return false;
        }

        if (kind == DecisionKind.AskObserve && askObserve is null)
        {
            reason = "missing-ask-observe";
            return false;
        }

        if (kind == DecisionKind.AskApproval && askApproval is null)
        {
            reason = "missing-ask-approval";
            return false;
        }

        if (kind == DecisionKind.Retry && retry is null)
        {
            reason = "missing-retry";
            return false;
        }

        if (kind == DecisionKind.Stop && stop is null)
        {
            reason = "missing-stop";
            return false;
        }

        nextActionDecision = new NextActionDecision
        {
            Kind = kind,
            Message = message ?? string.Empty,
            Rationale = rationale,
            Confidence = confidence,
            RequiresApprovalReason = requiresApprovalReason,
            RetryReason = retryReason,
            StopReason = stopReason,
            ExecuteAction = executeAction,
            AskObserve = askObserve,
            AskApproval = askApproval,
            Retry = retry,
            Stop = stop
        };

        reason = rationale ?? message;
        return true;
    }

    private static ExecuteActionPayload? ParseExecuteAction(JsonElement element, out string? reason)
    {
        reason = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryGetEnum(element, "actionType", out ActionType actionType))
        {
            reason = "invalid-execute-action";
            return null;
        }

        var target = element.TryGetProperty("target", out var targetElement)
            ? ParseActionTarget(targetElement, out reason)
            : new ActionTarget();
        if (reason is not null)
        {
            return null;
        }

        return new ExecuteActionPayload
        {
            ActionType = actionType,
            Target = target ?? new ActionTarget(),
            Parameters = new ActionParameters
            {
                Values = ParseDictionary(element, "parameters")
            },
            CapabilityHint = TryGetString(element, "capabilityHint"),
            RetryHint = TryGetString(element, "retryHint")
        };
    }

    private static ActionTarget? ParseActionTarget(JsonElement element, out string? reason)
    {
        reason = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            reason = "invalid-action-target";
            return null;
        }

        if (!TryGetEnum(element, "kind", out ActionTargetKind kind))
        {
            kind = ActionTargetKind.Unknown;
        }

        return new ActionTarget
        {
            Kind = kind,
            Reference = TryGetString(element, "reference") ?? string.Empty,
            DisplayName = TryGetString(element, "displayName"),
            Metadata = ParseDictionary(element, "metadata")
        };
    }

    private static AskObservePayload? ParseAskObserve(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new AskObservePayload
        {
            ObservationRequest = TryGetString(element, "observationRequest") ?? string.Empty,
            ObservationHint = TryGetString(element, "observationHint")
        };
    }

    private static AskApprovalPayload? ParseAskApproval(JsonElement element, out string? reason)
    {
        reason = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var proposedAction = element.TryGetProperty("proposedAction", out var proposedActionElement)
            ? ParseExecuteAction(proposedActionElement, out reason)
            : null;
        if (reason is not null)
        {
            return null;
        }

        return new AskApprovalPayload
        {
            RequiresApprovalReason = TryGetString(element, "requiresApprovalReason"),
            ProposedAction = proposedAction
        };
    }

    private static RetryPayload? ParseRetry(JsonElement element, out string? reason)
    {
        reason = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var action = element.TryGetProperty("action", out var actionElement)
            ? ParseExecuteAction(actionElement, out reason)
            : null;
        if (reason is not null)
        {
            return null;
        }

        return new RetryPayload
        {
            Action = action,
            RetryReason = TryGetString(element, "retryReason"),
            RetryCountHint = TryGetInt32(element, "retryCountHint")
        };
    }

    private static StopPayload? ParseStop(JsonElement element, out string? reason)
    {
        reason = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetEnum(element, "disposition", out StopDisposition disposition))
        {
            reason = "invalid-stop-disposition";
            return null;
        }

        return new StopPayload
        {
            Disposition = disposition,
            StopReason = TryGetString(element, "stopReason")
        };
    }

    private static Dictionary<string, string> ParseDictionary(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var jsonProperty in property.EnumerateObject())
        {
            if (jsonProperty.Value.ValueKind == JsonValueKind.String)
            {
                var value = jsonProperty.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values[jsonProperty.Name] = value;
                }
            }
            else if (jsonProperty.Value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)
            {
                values[jsonProperty.Name] = jsonProperty.Value.ToString();
            }
        }

        return values;
    }

    private static bool TryGetEnum<TEnum>(JsonElement element, string propertyName, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        var raw = TryGetString(element, propertyName);
        return !string.IsNullOrWhiteSpace(raw) &&
               Enum.TryParse(raw, ignoreCase: true, out value);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.TryGetDouble(out var value)
            ? value
            : null;
    }
}
