using System.Diagnostics;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class OpenAppActionHandler : IActionHandler
{
    public string ActionName => "open_app";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ActionParameterReader.GetTargetOrParameter(
            action, "app", "name", "application", "executable", "program", "target");

        if (!AppLaunchCatalog.ResolveForOpenApp(target, out var executable, out var error))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = error! });
        }

        var launchPath = AppLaunchCatalog.TryFindInstalledExecutable(executable) ?? executable;

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
            var hint = string.Equals(launchPath, executable, StringComparison.OrdinalIgnoreCase)
                ? AppLaunchCatalog.TryFindInstalledExecutable(executable) is null
                    ? " Kurulum yolu bulunamadi; 'launch' ile tam yol veya 'shell' ile keşif deneyin."
                    : string.Empty
                : string.Empty;

            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Uygulama '{executable}' baslatilamadi: {ex.Message}.{hint}"
            });
        }
    }
}
