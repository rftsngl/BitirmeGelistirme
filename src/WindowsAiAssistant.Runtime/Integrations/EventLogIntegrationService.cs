using System.Diagnostics;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class EventLogIntegrationService : IEventLogService
{
    public ActionResult Execute(string mode, string? logName = null, string? level = null, int hours = 1, int maxEntries = 50)
    {
        var normalized = (mode ?? "read").Trim().ToLowerInvariant();
        return normalized switch
        {
            "list" => ListLogs(),
            "read" => ReadLogs(logName, level, hours, maxEntries),
            _ => IntegrationResultHelper.Fail($"Desteklenen modlar: list, read. Verilen: {mode}")
        };
    }

    private static ActionResult ListLogs()
    {
        try
        {
            var builder = new StringBuilder();
            foreach (EventLog log in EventLog.GetEventLogs())
            {
                using (log)
                {
                    builder.AppendLine($"{log.Log} | entries={log.Entries.Count}");
                }
            }

            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Event log listesi alinamadi: {ex.Message}");
        }
    }

    private static ActionResult ReadLogs(string? logName, string? level, int hours, int maxEntries)
    {
        var name = string.IsNullOrWhiteSpace(logName) ? "Application" : logName.Trim();
        var since = DateTime.Now.AddHours(-Math.Clamp(hours, 1, 168));
        var limit = Math.Clamp(maxEntries, 1, 200);
        var minLevel = ParseLevel(level);

        try
        {
            using var log = new EventLog(name);
            var builder = new StringBuilder();
            builder.AppendLine($"log={name}");
            builder.AppendLine($"since={since:O}");
            builder.AppendLine($"level>={minLevel}");

            var count = 0;
            for (var i = log.Entries.Count - 1; i >= 0 && count < limit; i--)
            {
                var entry = log.Entries[i];
                if (entry.TimeGenerated < since)
                {
                    break;
                }

                if ((int)entry.EntryType < (int)minLevel)
                {
                    continue;
                }

                count++;
                builder.AppendLine("---");
                builder.AppendLine($"time={entry.TimeGenerated:O}");
                builder.AppendLine($"type={entry.EntryType}");
                builder.AppendLine($"source={entry.Source}");
                builder.AppendLine($"eventId={entry.InstanceId}");
                builder.AppendLine($"message={entry.Message.Replace('\r', ' ').Replace('\n', ' ')}");
            }

            builder.AppendLine($"count={count}");
            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Event log okunamadi: {ex.Message}");
        }
    }

    private static EventLogEntryType ParseLevel(string? level) =>
        (level ?? "error").Trim().ToLowerInvariant() switch
        {
            "information" or "info" => EventLogEntryType.Information,
            "warning" or "warn" => EventLogEntryType.Warning,
            "error" => EventLogEntryType.Error,
            "failureaudit" => EventLogEntryType.FailureAudit,
            "successaudit" => EventLogEntryType.SuccessAudit,
            _ => EventLogEntryType.Error
        };
}
