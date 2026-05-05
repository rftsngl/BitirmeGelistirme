using System.Text.Json;
using System.Threading;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;

namespace WindowsAiAssistant.Infrastructure;

public sealed class FileAuditLogger : IAuditLogger
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileAuditLogger()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAiAssistant",
            "logs");

        _filePath = Path.Combine(baseDir, "audit.jsonl");
    }

    public async Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = JsonSerializer.Serialize(auditEvent) + Environment.NewLine;

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(_filePath, line, cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch
        {
            // Fail-safe: audit write failures must not break command flow.
        }
    }
}
