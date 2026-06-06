using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class ListWindowsActionHandler : IActionHandler
{
    private readonly WindowManager _windowManager;

    public ListWindowsActionHandler(WindowManager windowManager) =>
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

    public string ActionName => "list_windows";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windows = _windowManager.ListVisibleWindows();
        if (windows.Count == 0)
        {
            return Task.FromResult(new ActionResult { Success = true, Message = "Gorunur pencere bulunamadi." });
        }

        var lines = windows.Select(window => window.ToPromptLine());
        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = string.Join(Environment.NewLine, lines)
        });
    }
}
