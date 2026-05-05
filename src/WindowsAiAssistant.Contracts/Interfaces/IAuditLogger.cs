using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Contracts.Interfaces;

public interface IAuditLogger
{
    Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
