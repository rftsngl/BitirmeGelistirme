using System.Diagnostics;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class OpenAppActionHandler : IActionHandler
{
    private readonly WindowManager _windowManager;

    public OpenAppActionHandler(WindowManager windowManager) =>
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

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

        if (AppLaunchCatalog.TryResolveProcessNamesForOpenApp(target, out var processNames))
        {
            var existing = _windowManager.ListVisibleWindows()
                .FirstOrDefault(window =>
                    processNames.Any(process =>
                        window.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase)));

            if (existing is not null)
            {
                var focused = _windowManager.FocusWindow(existing.Handle);
                return Task.FromResult(new ActionResult
                {
                    Success = focused,
                    Message = focused
                        ? $"Uygulama zaten acik; pencere odaklandi: [{existing.WindowId}] {existing.Title} (open_app atlandi)."
                        : $"Uygulama acik ancak odaklanamadi: [{existing.WindowId}] {existing.Title}"
                });
            }
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
