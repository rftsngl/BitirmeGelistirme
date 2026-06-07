using System.Diagnostics;
using System.Text;
using WindowsAiAssistant.Runtime.Actions;

namespace WindowsAiAssistant.Runtime.Integrations;

public sealed class PackageInstallService : IPackageInstallService
{
    private const int TimeoutMs = 120000;

    public ActionResult Execute(string mode, string? packageId = null, string? source = null)
    {
        var normalized = (mode ?? "list").Trim().ToLowerInvariant();
        return normalized switch
        {
            "search" => RunWinget($"search \"{EscapeArg(packageId)}\" --accept-source-agreements", packageId),
            "install" when string.IsNullOrWhiteSpace(packageId) =>
                IntegrationResultHelper.Fail("install icin target veya parameters.id gerekli."),
            "install" => RunWinget(BuildInstallCommand(packageId, source), packageId),
            "uninstall" => RunWinget($"uninstall \"{EscapeArg(packageId)}\" --accept-source-agreements", packageId),
            "list" => RunWinget("list --accept-source-agreements", null),
            "list_store" => RunPowerShell("Get-AppxPackage | Select-Object Name, PackageFullName, Version | Format-Table -AutoSize | Out-String -Width 200"),
            _ => IntegrationResultHelper.Fail($"Desteklenen modlar: search, install, uninstall, list, list_store. Verilen: {mode}")
        };
    }

    private static string BuildInstallCommand(string packageId, string? source)
    {
        var cmd = $"install \"{EscapeArg(packageId)}\" --accept-package-agreements --accept-source-agreements";
        if (!string.IsNullOrWhiteSpace(source))
        {
            cmd += $" --source {source.Trim()}";
        }

        return cmd;
    }

    private static ActionResult RunWinget(string arguments, string? packageId)
    {
        if (arguments.Contains("search", StringComparison.Ordinal) ||
            arguments.Contains("install", StringComparison.Ordinal) ||
            arguments.Contains("uninstall", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return IntegrationResultHelper.Fail("Bu mod icin target veya parameters.id gerekli.");
            }
        }

        return RunProcess("winget", arguments);
    }

    private static ActionResult RunPowerShell(string script) =>
        RunProcess("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"");

    private static ActionResult RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return IntegrationResultHelper.Fail($"{fileName} zaman asimina ugradi.");
            }

            var builder = new StringBuilder();
            builder.AppendLine($"exitCode={process.ExitCode}");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                builder.AppendLine(stdout);
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                builder.AppendLine("stderr:");
                builder.AppendLine(stderr);
            }

            return new ActionResult
            {
                Success = process.ExitCode == 0,
                Message = IntegrationResultHelper.Truncate(builder.ToString())
            };
        }
        catch (Exception ex)
        {
            return IntegrationResultHelper.Fail($"{fileName} calistirilamadi: {ex.Message}");
        }
    }

    private static string EscapeArg(string? value) =>
        (value ?? string.Empty).Replace("\"", "\\\"", StringComparison.Ordinal);
}
