using System.Diagnostics;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class LaunchActionHandler : IActionHandler
{
    public string ActionName => "launch";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ActionParameterReader.GetTargetOrParameter(
            action, "app", "command", "uri", "name", "path",
            "application", "executable", "program", "file", "target");
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "launch icin target veya parameters.app/command/uri gerekli."
            });
        }

        if (AppLaunchCatalog.TryResolve(target, out var catalogExecutable, out _))
        {
            target = catalogExecutable;
        }

        var launchPath = AppLaunchCatalog.TryFindInstalledExecutable(target) ?? target;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = launchPath,
                UseShellExecute = true
            });

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"{launchPath} baslatildi."
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Uygulama baslatilamadi: {ex.Message}"
            });
        }
    }
}
