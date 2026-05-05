using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public sealed class NoOpCapability : ICapability
{
    public string Name => "NoOpCapability";

    public IReadOnlyCollection<TargetKind> SupportedTargetKinds { get; } =
    [
        TargetKind.Unknown,
        TargetKind.Application,
        TargetKind.Service,
        TargetKind.Window,
        TargetKind.Process,
        TargetKind.UiElement
    ];

    public bool CanHandle(AgentAction action, AgentExecutionContext context)
    {
        return true;
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        AgentAction action,
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = DateTimeOffset.UtcNow;

        var result = new ActionExecutionResult
        {
            Status = ExecutionStatus.Skipped,
            Message = "NoOpCapability invoked. Real capability implementation does not exist yet.",
            IsVerified = false,
            OutputText = null,
            OutputData = null,
            ErrorCode = null,
            UsedFallback = true,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt
        };

        return Task.FromResult(result);
    }
}
