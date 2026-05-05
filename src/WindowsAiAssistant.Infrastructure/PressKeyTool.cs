using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;
using WindowsAiAssistant.Infrastructure.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure;

public sealed class PressKeyTool : ITool
{
    private readonly IActionPrimitiveExecutor _primitiveExecutor;

    public PressKeyTool(IActionPrimitiveExecutor primitiveExecutor)
    {
        _primitiveExecutor = primitiveExecutor ?? throw new ArgumentNullException(nameof(primitiveExecutor));
    }

    public string Name => "PressKeyTool";

    public async Task<ToolResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var keyText = ExtractKeyPayload(request.UserInput);

        if (!KeyboardInputParser.TryParseKey(keyText, out var parsedKey))
        {
            return new ToolResult
            {
                Success = false,
                Output = "Blocked execution: key payload could not be parsed.",
                Message = "PressKeyTool blocked: unsupported key token."
            };
        }

        var primitiveRequest = new ActionPrimitiveExecutionRequest
        {
            CorrelationId = request.CorrelationId,
            Primitive = new PressKeyPrimitive(parsedKey),
            RequestedAtUtc = request.RequestedAtUtc
        };

        var primitiveResult = await _primitiveExecutor.ExecuteAsync(primitiveRequest, cancellationToken);
        return MapToolResult(primitiveResult);
    }

    private static string ExtractKeyPayload(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return string.Empty;
        }

        var trimmed = rawInput.Trim();

        if (trimmed.StartsWith("press key ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[10..].Trim();
        }

        if (trimmed.StartsWith("press ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[6..].Trim();
        }

        if (trimmed.StartsWith("key ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[4..].Trim();
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
