using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.ActionPrimitives;

namespace WindowsAiAssistant.Infrastructure;

public sealed class TypeTextTool : ITool
{
    private readonly IActionPrimitiveExecutor _primitiveExecutor;

    public TypeTextTool(IActionPrimitiveExecutor primitiveExecutor)
    {
        _primitiveExecutor = primitiveExecutor ?? throw new ArgumentNullException(nameof(primitiveExecutor));
    }

    public string Name => "TypeTextTool";

    public async Task<ToolResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var text = ExtractTextPayload(request.UserInput);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ToolResult
            {
                Success = false,
                Output = "Blocked execution: text payload is empty.",
                Message = "TypeTextTool blocked: text payload was not provided."
            };
        }

        var primitiveRequest = new ActionPrimitiveExecutionRequest
        {
            CorrelationId = request.CorrelationId,
            Primitive = new TypeTextPrimitive(text),
            RequestedAtUtc = request.RequestedAtUtc
        };

        var primitiveResult = await _primitiveExecutor.ExecuteAsync(primitiveRequest, cancellationToken);
        return MapToolResult(primitiveResult);
    }

    private static string ExtractTextPayload(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return string.Empty;
        }

        var trimmed = rawInput.Trim();

        if (trimmed.StartsWith("type ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[5..];
        }

        if (trimmed.StartsWith("write ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[6..];
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
