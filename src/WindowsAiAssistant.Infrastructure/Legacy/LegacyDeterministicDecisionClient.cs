using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure.Legacy;

public sealed class LegacyDeterministicDecisionClient : IAiDecisionClient
{
    public Task<AiDecision> DecideAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var effectiveInput = GetEffectiveInput(request);
        if (TryClassifyPrimitiveInputIntent(effectiveInput, out var primitiveDecision))
        {
            return Task.FromResult(primitiveDecision);
        }

        return Task.FromResult(CreateNoMatchDecision());
    }

    private static string GetEffectiveInput(CommandRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.AiDecisionInput?.RawInput))
        {
            return request.AiDecisionInput.RawInput.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.AiDecisionInput?.NormalizedInput))
        {
            return request.AiDecisionInput.NormalizedInput.Trim();
        }

        return (request.UserInput ?? string.Empty).Trim();
    }

    private static bool TryClassifyPrimitiveInputIntent(string input, out AiDecision decision)
    {
        if (TryExtractTypeTextPayload(input, out var textPayload))
        {
            decision = CreatePrimitiveDecision(
                ActionType.InputText,
                "TypeTextTool",
                textPayload,
                "Legacy deterministic fallback matched: type text primitive command");

            return true;
        }

        if (TryExtractShortcutPayload(input, out var shortcutPayload))
        {
            decision = CreatePrimitiveDecision(
                ActionType.PressShortcut,
                "PressShortcutTool",
                shortcutPayload,
                "Legacy deterministic fallback matched: keyboard shortcut primitive command");

            return true;
        }

        if (TryExtractKeyPayload(input, out var keyPayload))
        {
            decision = CreatePrimitiveDecision(
                ActionType.PressKey,
                "PressKeyTool",
                keyPayload,
                "Legacy deterministic fallback matched: key press primitive command");

            return true;
        }

        decision = CreateNoMatchDecision();
        return false;
    }

    private static AiDecision CreatePrimitiveDecision(
        ActionType actionType,
        string selectedToolName,
        string toolArgument,
        string decisionText)
    {
        return new AiDecision
        {
            SelectedToolName = selectedToolName,
            ToolArgument = toolArgument,
            DecisionText = decisionText,
            IsLegacyFallback = true,
            NextActionDecision = new NextActionDecision
            {
                Kind = DecisionKind.ExecuteAction,
                Message = decisionText,
                Rationale = "legacy-deterministic-fallback",
                Confidence = 1.0,
                ExecuteAction = new ExecuteActionPayload
                {
                    ActionType = actionType,
                    Target = new ActionTarget
                    {
                        Kind = ActionTargetKind.Generic,
                        Reference = "active-context",
                        DisplayName = "active-context"
                    },
                    Parameters = new ActionParameters
                    {
                        Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [actionType == ActionType.InputText ? "text" : actionType == ActionType.PressShortcut ? "shortcut" : "key"] = toolArgument
                        }
                    }
                }
            }
        };
    }

    private static AiDecision CreateNoMatchDecision()
    {
        const string decisionText = "No legacy deterministic primitive rule matched";

        return new AiDecision
        {
            SelectedToolName = string.Empty,
            ToolArgument = null,
            DecisionText = decisionText,
            IsLegacyFallback = true,
            NextActionDecision = new NextActionDecision
            {
                Kind = DecisionKind.Stop,
                Message = decisionText,
                Rationale = "legacy-deterministic-fallback",
                StopReason = "legacy-no-primitive-match",
                Stop = new StopPayload
                {
                    Disposition = StopDisposition.Completed,
                    StopReason = "No deterministic primitive action matched for the input."
                }
            }
        };
    }

    private static bool TryExtractTypeTextPayload(string input, out string payload)
    {
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (input.StartsWith("type ", StringComparison.OrdinalIgnoreCase))
        {
            payload = input[5..].Trim();
            return payload.Length > 0;
        }

        if (input.StartsWith("write ", StringComparison.OrdinalIgnoreCase))
        {
            payload = input[6..].Trim();
            return payload.Length > 0;
        }

        return false;
    }

    private static bool TryExtractShortcutPayload(string input, out string payload)
    {
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string candidate;

        if (input.StartsWith("press shortcut ", StringComparison.OrdinalIgnoreCase))
        {
            candidate = input[15..];
        }
        else if (input.StartsWith("shortcut ", StringComparison.OrdinalIgnoreCase))
        {
            candidate = input[9..];
        }
        else if (input.StartsWith("press ", StringComparison.OrdinalIgnoreCase) &&
                 !input.StartsWith("press key ", StringComparison.OrdinalIgnoreCase))
        {
            candidate = input[6..];
        }
        else
        {
            return false;
        }

        var normalized = candidate.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized.Length == 0 || !normalized.Contains('+'))
        {
            return false;
        }

        payload = normalized;
        return true;
    }

    private static bool TryExtractKeyPayload(string input, out string payload)
    {
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string candidate;

        if (input.StartsWith("press key ", StringComparison.OrdinalIgnoreCase))
        {
            candidate = input[10..];
        }
        else if (input.StartsWith("key ", StringComparison.OrdinalIgnoreCase))
        {
            candidate = input[4..];
        }
        else if (input.StartsWith("press ", StringComparison.OrdinalIgnoreCase))
        {
            candidate = input[6..];
        }
        else
        {
            return false;
        }

        var normalized = candidate.Trim();
        if (normalized.Length == 0 || normalized.Contains('+'))
        {
            return false;
        }

        if (normalized.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        payload = normalized;
        return true;
    }
}
