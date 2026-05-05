using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ITool
{
    string Name { get; }

    Task<ToolResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default);
}
