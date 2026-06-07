using System.Diagnostics;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class NotificationListenerService : INotificationListenerService
{
    public Task<ActionResult> ExecuteAsync(
        string mode,
        int maxEntries = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = (mode ?? "peek").Trim().ToLowerInvariant();
        return normalized switch
        {
            "request_access" => Task.FromResult(IntegrationResultHelper.Ok(
                "Bildirim okuma: Windows Ayarlar > Gizlilik ve guvenlik > Bildirimler > " +
                "Windows AI Assistant icin erisim acin. Sonra peek modunu kullanin.")),
            "peek" or "read" => Task.FromResult(Peek(maxEntries)),
            _ => Task.FromResult(IntegrationResultHelper.Fail("Desteklenen modlar: request_access, peek."))
        };
    }

    private static ActionResult Peek(int maxEntries)
    {
        var limit = Math.Clamp(maxEntries, 1, 50);
        var script = """
            $logs = @(
              'Microsoft-Windows-Shell-Core/Operational',
              'Microsoft-Windows-ActionCenter/Operational'
            )
            $events = foreach ($log in $logs) {
              try {
                Get-WinEvent -LogName $log -MaxEvents __MAX__ -ErrorAction Stop |
                  Select-Object TimeCreated, Id, ProviderName, Message
              } catch { }
            }
            $events | Sort-Object TimeCreated -Descending | Select-Object -First __MAX__ |
            Format-List | Out-String -Width 220
            """.Replace("__MAX__", limit.ToString(), StringComparison.Ordinal);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return IntegrationResultHelper.Fail("Bildirim okuma zaman asimina ugradi.");
            }

            var builder = new StringBuilder();
            builder.AppendLine("source=ActionCenter/Shell event logs");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                builder.AppendLine(stdout);
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                builder.AppendLine("stderr:");
                builder.AppendLine(stderr);
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                builder.AppendLine("(kayit bulunamadi veya log kapali)");
            }

            return IntegrationResultHelper.Ok(builder);
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"Bildirimler okunamadi: {ex.Message}");
        }
    }
}
