using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;
using WindowsAiAssistant.Infrastructure.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure;

public sealed class PressShortcutTool : ITool
{
    private readonly IActionPrimitiveExecutor _primitiveExecutor;

    public PressShortcutTool(IActionPrimitiveExecutor primitiveExecutor)
    {
        _primitiveExecutor = primitiveExecutor ?? throw new ArgumentNullException(nameof(primitiveExecutor));
    }

    public string Name => "PressShortcutTool";

    public async Task<ToolResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var shortcutText = ExtractShortcutPayload(request.UserInput);

        if (!KeyboardInputParser.TryParseShortcut(shortcutText, out var key, out var modifiers))
        {
            return new ToolResult
            {
                Success = false,
                Output = "Blocked execution: shortcut payload could not be parsed.",
                Message = "PressShortcutTool blocked: unsupported shortcut token."
            };
        }

        var primitiveRequest = new ActionPrimitiveExecutionRequest
        {
            CorrelationId = request.CorrelationId,
            Primitive = new PressShortcutPrimitive(key, modifiers),
            RequestedAtUtc = request.RequestedAtUtc
        };

        var primitiveResult = await _primitiveExecutor.ExecuteAsync(primitiveRequest, cancellationToken);
        return MapToolResult(primitiveResult);
    }

    private static string ExtractShortcutPayload(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return string.Empty;
        }

        var trimmed = rawInput.Trim();

        if (trimmed.StartsWith("press shortcut ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[15..].Trim();
        }

        if (trimmed.StartsWith("shortcut ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[9..].Trim();
        }

        if (trimmed.StartsWith("press ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[6..].Trim();
        }

        return trimmed;
    }

    private static ToolResult MapToolResult(ActionPrimitiveExecutionResult primitiveResult)
    {
        return new ToolResult
        {
            Success = primitiveResult.Success,
            Output = primitiveResult.OutputText,
            Message = primitiveResult.Message,
            PrimitiveExecution = primitiveResult
        };
    }
}
