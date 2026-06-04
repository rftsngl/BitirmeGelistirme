using System.Diagnostics;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class OpenAppActionHandler : IActionHandler
{
    public string ActionName => "open_app";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = action.Target;
        if (string.IsNullOrWhiteSpace(target) &&
            action.Parameters.TryGetValue("app", out var appParameter))
        {
            target = appParameter;
        }

        if (!AppLaunchCatalog.TryResolve(target, out var executable, out var error))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = error! });
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true
            });

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"{executable} baslatildi."
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
