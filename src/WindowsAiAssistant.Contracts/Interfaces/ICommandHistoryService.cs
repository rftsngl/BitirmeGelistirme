using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ICommandHistoryService
{
    Task<IReadOnlyList<RecentCommandSummary>> GetRecentAsync(int maxCount, CancellationToken cancellationToken = default);
}
