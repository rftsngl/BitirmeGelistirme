using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using AgentExecutionContext = WindowsAiAssistant.Contracts.Models.Agent.ExecutionContext;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ICapability
{
    string Name { get; }
    IReadOnlyCollection<TargetKind> SupportedTargetKinds { get; }

    bool CanHandle(AgentAction action, AgentExecutionContext context);

    Task<ActionExecutionResult> ExecuteAsync(
        AgentAction action,
        AgentExecutionContext context,
        CancellationToken cancellationToken = default);
}
