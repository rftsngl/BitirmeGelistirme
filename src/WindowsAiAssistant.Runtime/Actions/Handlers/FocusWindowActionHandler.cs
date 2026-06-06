using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class FocusWindowActionHandler : IActionHandler
{
    private readonly WindowManager _windowManager;

    public FocusWindowActionHandler(WindowManager windowManager) =>
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

    public string ActionName => "focus_window";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ActionParameterReader.GetTargetOrParameter(action, "windowId", "title");
        var windows = _windowManager.ListVisibleWindows();
        if (!_windowManager.TryResolveWindow(target, windows, out var window, out var error) || window is null)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = error! });
        }

        var focused = _windowManager.FocusWindow(window.Handle);
        return Task.FromResult(new ActionResult
        {
            Success = focused,
            Message = focused
                ? $"Pencere odaklandi: [{window.WindowId}] {window.Title}"
                : $"Pencere odaklanamadi: [{window.WindowId}] {window.Title}"
        });
    }
}
