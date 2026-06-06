using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class MoveWindowActionHandler : IActionHandler
{
    private readonly WindowManager _windowManager;

    public MoveWindowActionHandler(WindowManager windowManager) =>
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

    public string ActionName => "move_window";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ActionParameterReader.GetTargetOrParameter(action, "windowId", "title");
        if (!ActionParameterReader.TryGetInt(action, "x", out var x) ||
            !ActionParameterReader.TryGetInt(action, "y", out var y) ||
            !ActionParameterReader.TryGetInt(action, "width", out var width) ||
            !ActionParameterReader.TryGetInt(action, "height", out var height))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "move_window icin parameters.x, y, width, height (tamsayi) gerekli."
            });
        }

        var windows = _windowManager.ListVisibleWindows();
        if (!_windowManager.TryResolveWindow(target, windows, out var window, out var error) || window is null)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = error! });
        }

        var moved = _windowManager.MoveWindow(window.Handle, x, y, width, height);
        return Task.FromResult(new ActionResult
        {
            Success = moved,
            Message = moved
                ? $"Pencere tasindi: [{window.WindowId}] -> {x},{y} {width}x{height}"
                : $"Pencere tasinamadi: [{window.WindowId}]"
        });
    }
}
