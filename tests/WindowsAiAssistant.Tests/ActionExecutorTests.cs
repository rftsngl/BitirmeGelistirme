using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Tests;

public sealed class ActionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UnknownAction_ReturnsUnsupportedErrorCode()
    {
        var executor = new ActionExecutor(Array.Empty<IActionHandler>());
        var result = await executor.ExecuteAsync(new AgentAction { Action = "missing_action" });

        Assert.False(result.Success);
        Assert.Equal(ActionFailureCodes.UnsupportedAction, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerThrows_ReturnsStructuredFailure()
    {
        var executor = new ActionExecutor([new ThrowingHandler()]);
        var result = await executor.ExecuteAsync(new AgentAction { Action = "throw_test" });

        Assert.False(result.Success);
        Assert.Equal(ActionFailureCodes.HandlerException, result.ErrorCode);
        Assert.Equal(nameof(InvalidOperationException), result.ExceptionType);
        Assert.Contains("boom", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", result.Message, StringComparison.Ordinal);
    }

    private sealed class ThrowingHandler : IActionHandler
    {
        public string ActionName => "throw_test";

        public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
