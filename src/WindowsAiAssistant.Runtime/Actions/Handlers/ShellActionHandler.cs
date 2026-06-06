using System.Diagnostics;
using System.Text;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

/// <summary>
/// Genel kabuk yetenegi: PowerShell veya CMD ile komut calistirir; cikis kodu,
/// stdout ve stderr'i yapilandirilmis sekilde dondurur. Cikti bir sonraki adimin
/// gozlemine (lastActionResult) tasinir, boylece LLM kesif/yurutme dongusu kurabilir.
/// </summary>
public sealed class ShellActionHandler : IActionHandler
{
    private const int DefaultTimeoutMs = 20000;
    private const int MaxTimeoutMs = 120000;
    private const int MaxOutputChars = 4000;

    public string ActionName => "shell";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = ActionParameterReader.GetTargetOrParameter(action, "command", "cmd", "script");
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ActionResult
            {
                Success = false,
                Message = "shell icin target veya parameters.command gerekli."
            };
        }

        var shell = ReadShell(action);
        if (shell is null)
        {
            return new ActionResult
            {
                Success = false,
                Message = "Desteklenen kabuklar: powershell, pwsh veya cmd."
            };
        }

        var timeoutMs = ActionParameterReader.TryGetInt(action, "timeoutMs", out var ms)
            ? Math.Clamp(ms, 1000, MaxTimeoutMs)
            : DefaultTimeoutMs;

        var startInfo = BuildStartInfo(shell, command);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            if (!process.Start())
            {
                return new ActionResult { Success = false, Message = "Kabuk sureci baslatilamadi." };
            }
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Kabuk sureci baslatilamadi: {ex.Message}" };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ActionResult
            {
                Success = false,
                Message = $"Komut zaman asimina ugradi ({timeoutMs} ms). Sonlandirildi. {FormatOutput(null, stdout, stderr)}"
            };
        }

        var exitCode = process.ExitCode;
        var outText = stdout.ToString().Trim();
        var message = FormatOutput(exitCode, stdout, stderr);
        var discoveryMiss = exitCode == 0 &&
                            string.IsNullOrWhiteSpace(outText) &&
                            LooksLikeDiscoveryCommand(command);
        if (discoveryMiss)
        {
            message += " | not: PATH/komut ciktisi bos — open_app veya launch ile kurulum yolunu deneyin.";
        }

        return new ActionResult
        {
            Success = exitCode == 0 && !discoveryMiss,
            Message = message
        };
    }

    private static string? ReadShell(AgentAction action)
    {
        if (!action.Parameters.TryGetValue("shell", out var shell) || string.IsNullOrWhiteSpace(shell))
        {
            return "powershell";
        }

        return shell.Trim().ToLowerInvariant() switch
        {
            "powershell" or "powershell.exe" => "powershell",
            "pwsh" or "pwsh.exe" => "pwsh",
            "cmd" or "cmd.exe" => "cmd",
            _ => null
        };
    }

    private static ProcessStartInfo BuildStartInfo(string shell, string command)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (shell == "cmd")
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c chcp 65001>nul & {command}";
        }
        else
        {
            startInfo.FileName = shell == "pwsh" ? "pwsh.exe" : "powershell.exe";
            var script = "$ProgressPreference='SilentlyContinue'; " +
                "$OutputEncoding=[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            startInfo.Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand {encoded}";
        }

        return startInfo;
    }

    private static string FormatOutput(int? exitCode, StringBuilder stdout, StringBuilder stderr)
    {
        var builder = new StringBuilder();
        builder.Append("exitCode=").Append(exitCode?.ToString() ?? "?");

        var outText = Truncate(stdout.ToString().Trim());
        var errText = Truncate(stderr.ToString().Trim());

        builder.Append(" | stdout: ").Append(string.IsNullOrEmpty(outText) ? "(bos)" : outText);
        if (!string.IsNullOrEmpty(errText))
        {
            builder.Append(" | stderr: ").Append(errText);
        }

        return builder.ToString();
    }

    private static string Truncate(string value) =>
        value.Length <= MaxOutputChars
            ? value
            : value[..MaxOutputChars] + $"... (+{value.Length - MaxOutputChars} karakter kesildi)";

    private static bool LooksLikeDiscoveryCommand(string command)
    {
        var normalized = command.Trim();
        return normalized.Contains(" where ", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("where ", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("Get-Command", StringComparison.OrdinalIgnoreCase) ||
               (normalized.Contains("Get-ItemProperty", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains("App Paths", StringComparison.OrdinalIgnoreCase));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Sonlandirma hatalari yutulur; sonuc zaten basarisiz olarak dönecek.
        }
    }
}
