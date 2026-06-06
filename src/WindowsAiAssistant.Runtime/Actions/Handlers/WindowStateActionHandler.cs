using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class WindowStateActionHandler : IActionHandler
{
    private readonly WindowManager _windowManager;

    public WindowStateActionHandler(WindowManager windowManager) =>
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

    public string ActionName => "window_state";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ActionParameterReader.GetTargetOrParameter(action, "windowId", "title");
        var state = ActionParameterReader.GetTargetOrParameter(action, "state");
        if (string.IsNullOrWhiteSpace(state))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "window_state icin parameters.state gerekli (minimize|maximize|restore|close)."
            });
        }

        var windows = _windowManager.ListVisibleWindows();
        if (!_windowManager.TryResolveWindow(target, windows, out var window, out var error) || window is null)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = error! });
        }

        var applied = _windowManager.SetWindowState(window.Handle, state);
        return Task.FromResult(new ActionResult
        {
            Success = applied,
            Message = applied
                ? $"Pencere durumu guncellendi: [{window.WindowId}] -> {state}"
                : $"Gecersiz pencere durumu veya islem basarisiz: {state}"
        });
    }
}
