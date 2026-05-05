using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure.ActionPrimitives;

public sealed class WindowsNativeInputPrimitiveHandler : IActionPrimitiveHandler
{
    private readonly IKeyboardInputGateway _keyboardInputGateway;

    public WindowsNativeInputPrimitiveHandler(IKeyboardInputGateway keyboardInputGateway)
    {
        _keyboardInputGateway = keyboardInputGateway ?? throw new ArgumentNullException(nameof(keyboardInputGateway));
    }

    public bool CanHandle(ActionPrimitive primitive)
    {
        return primitive.Kind is ActionPrimitiveKind.TypeText or ActionPrimitiveKind.PressKey or ActionPrimitiveKind.PressShortcut;
    }

    public Task<ActionPrimitiveExecutionResult> ExecuteAsync(
        ActionPrimitiveExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var primitive = request.Primitive;

        if (primitive is null)
        {
            return Task.FromResult(CreateBlocked(
                ActionPrimitiveKind.TypeText,
                startedAt,
                "Input primitive blocked: payload was not supplied.",
                "Primitive payload is missing."));
        }

        var foregroundHandle = _keyboardInputGateway.GetForegroundWindowHandle();
        if (foregroundHandle == nint.Zero)
        {
            return Task.FromResult(CreateBlocked(
                primitive.Kind,
                startedAt,
                "Input primitive blocked: there is no focused window.",
                "No focused window available for input dispatch."));
        }

        return primitive switch
        {
            TypeTextPrimitive typeTextPrimitive => Task.FromResult(ExecuteTypeTextPrimitive(typeTextPrimitive, startedAt, foregroundHandle)),
            PressKeyPrimitive pressKeyPrimitive => Task.FromResult(ExecutePressKeyPrimitive(pressKeyPrimitive, startedAt, foregroundHandle)),
            PressShortcutPrimitive pressShortcutPrimitive => Task.FromResult(ExecutePressShortcutPrimitive(pressShortcutPrimitive, startedAt, foregroundHandle)),
            _ => Task.FromResult(CreateBlocked(
                primitive.Kind,
                startedAt,
                "Input primitive blocked: unsupported payload type.",
                "Unsupported input primitive payload type."))
        };
    }

    private ActionPrimitiveExecutionResult ExecuteTypeTextPrimitive(
        TypeTextPrimitive primitive,
        DateTimeOffset startedAt,
        nint foregroundHandle)
    {
        if (string.IsNullOrWhiteSpace(primitive.Text))
        {
            return CreateBlocked(
                primitive.Kind,
                startedAt,
                "TypeText primitive blocked: text payload is empty.",
                "Text payload is empty.",
                foregroundHandle);
        }

        var dispatchResult = _keyboardInputGateway.SendText(primitive.Text);
        if (!dispatchResult.Success)
        {
            return CreateDispatchFailure(
                primitive.Kind,
                startedAt,
                dispatchResult,
                "TypeText primitive failed during native dispatch.",
                foregroundHandle);
        }

        return CreateSuccess(
            primitive.Kind,
            startedAt,
            "TypeText primitive executed on the focused window.",
            $"Real text input sent to focused window ({primitive.Text.Length} chars).",
            dispatchResult,
            foregroundHandle,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["textLength"] = primitive.Text.Length.ToString(),
                ["containsWhitespace"] = primitive.Text.Any(char.IsWhiteSpace) ? "true" : "false"
            });
    }

    private ActionPrimitiveExecutionResult ExecutePressKeyPrimitive(
        PressKeyPrimitive primitive,
        DateTimeOffset startedAt,
        nint foregroundHandle)
    {
        var dispatchResult = _keyboardInputGateway.SendKey(primitive.Key);
        if (!dispatchResult.Success)
        {
            return CreateDispatchFailure(
                primitive.Kind,
                startedAt,
                dispatchResult,
                "PressKey primitive failed during native dispatch.",
                foregroundHandle);
        }

        return CreateSuccess(
            primitive.Kind,
            startedAt,
            "PressKey primitive executed on the focused window.",
            $"Real key press sent to focused window: {primitive.Key}.",
            dispatchResult,
            foregroundHandle,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["key"] = primitive.Key.ToString()
            });
    }

    private ActionPrimitiveExecutionResult ExecutePressShortcutPrimitive(
        PressShortcutPrimitive primitive,
        DateTimeOffset startedAt,
        nint foregroundHandle)
    {
        if (primitive.Modifiers is null || primitive.Modifiers.Count == 0)
        {
            return CreateBlocked(
                primitive.Kind,
                startedAt,
                "PressShortcut primitive blocked: at least one modifier key is required.",
                "Shortcut modifiers were not provided.",
                foregroundHandle);
        }

        var normalizedModifiers = primitive.Modifiers
            .Distinct()
            .ToArray();

        var dispatchResult = _keyboardInputGateway.SendShortcut(normalizedModifiers, primitive.Key);
        if (!dispatchResult.Success)
        {
            return CreateDispatchFailure(
                primitive.Kind,
                startedAt,
                dispatchResult,
                "PressShortcut primitive failed during native dispatch.",
                foregroundHandle);
        }

        return CreateSuccess(
            primitive.Kind,
            startedAt,
            "PressShortcut primitive executed on the focused window.",
            $"Real keyboard shortcut sent to focused window: {string.Join('+', normalizedModifiers)}+{primitive.Key}.",
            dispatchResult,
            foregroundHandle,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["key"] = primitive.Key.ToString(),
                ["modifiers"] = string.Join("+", normalizedModifiers)
            });
    }

    private static ActionPrimitiveExecutionResult CreateSuccess(
        ActionPrimitiveKind kind,
        DateTimeOffset startedAt,
        string message,
        string outputText,
        KeyboardDispatchResult dispatchResult,
        nint foregroundHandle,
        IDictionary<string, string>? additionalMetadata = null)
    {
        var metadata = BuildMetadata(dispatchResult, foregroundHandle, additionalMetadata);

        return new ActionPrimitiveExecutionResult
        {
            Success = true,
            PrimitiveKind = kind,
            Message = message,
            OutputText = outputText,
            ErrorCode = null,
            BlockedReason = null,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Metadata = metadata
        };
    }

    private static ActionPrimitiveExecutionResult CreateDispatchFailure(
        ActionPrimitiveKind kind,
        DateTimeOffset startedAt,
        KeyboardDispatchResult dispatchResult,
        string message,
        nint foregroundHandle)
    {
        var metadata = BuildMetadata(dispatchResult, foregroundHandle, null);

        return new ActionPrimitiveExecutionResult
        {
            Success = false,
            PrimitiveKind = kind,
            Message = message,
            OutputText = "Real input dispatch failed.",
            ErrorCode = dispatchResult.ErrorCode,
            BlockedReason = null,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Metadata = metadata
        };
    }

    private static ActionPrimitiveExecutionResult CreateBlocked(
        ActionPrimitiveKind kind,
        DateTimeOffset startedAt,
        string message,
        string blockedReason,
        nint? foregroundHandle = null)
    {
        Dictionary<string, string>? metadata = null;

        if (foregroundHandle.HasValue)
        {
            metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["foregroundWindowHandle"] = foregroundHandle.Value.ToString("X")
            };
        }

        return new ActionPrimitiveExecutionResult
        {
            Success = false,
            PrimitiveKind = kind,
            Message = message,
            OutputText = "Blocked execution: input primitive blocked.",
            ErrorCode = null,
            BlockedReason = blockedReason,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Metadata = metadata
        };
    }

    private static IDictionary<string, string> BuildMetadata(
        KeyboardDispatchResult dispatchResult,
        nint foregroundHandle,
        IDictionary<string, string>? additionalMetadata)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["foregroundWindowHandle"] = foregroundHandle.ToString("X"),
            ["expectedInputCount"] = dispatchResult.ExpectedInputCount.ToString(),
            ["sentInputCount"] = dispatchResult.SentInputCount.ToString(),
            ["nativeDispatchMessage"] = dispatchResult.Message
        };

        if (!string.IsNullOrWhiteSpace(dispatchResult.ErrorCode))
        {
            metadata["nativeDispatchErrorCode"] = dispatchResult.ErrorCode;
        }

        if (additionalMetadata is not null)
        {
            foreach (var (key, value) in additionalMetadata)
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }
}
