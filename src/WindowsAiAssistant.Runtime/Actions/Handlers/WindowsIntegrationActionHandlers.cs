using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.Runtime.Actions.Handlers;

public sealed class CaptureScreenActionHandler : IActionHandler
{
    private readonly IGraphicsCaptureService _capture;

    public CaptureScreenActionHandler(IGraphicsCaptureService capture) =>
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));

    public string ActionName => "capture_screen";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runId = ActionParameterReader.GetTargetOrParameter(action, "runId") ?? Guid.NewGuid().ToString("N");
        var stepIndex = ActionParameterReader.TryGetInt(action, "stepIndex", out var index) ? index : 0;
        var monitor = ActionParameterReader.GetTargetOrParameter(action, "monitor");
        var method = ActionParameterReader.GetTargetOrParameter(action, "method");

        return Task.FromResult(_capture.Capture(runId, stepIndex, monitor, method));
    }
}

public sealed class NotifyActionHandler : IActionHandler
{
    private readonly IToastNotificationService _toast;

    public NotifyActionHandler(IToastNotificationService toast) =>
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));

    public string ActionName => "notify";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var title = ActionParameterReader.GetTargetOrParameter(action, "title") ?? "Windows AI Assistant";
        var body = ActionParameterReader.GetTargetOrParameter(action, "message", "body", "text");
        if (string.IsNullOrWhiteSpace(body))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = "notify icin parameters.message veya parameters.body gerekli."
            });
        }

        return Task.FromResult(_toast.Show(title, body));
    }
}

public sealed class WmiQueryActionHandler : IActionHandler
{
    private readonly IWmiQueryService _wmi;

    public WmiQueryActionHandler(IWmiQueryService wmi) =>
        _wmi = wmi ?? throw new ArgumentNullException(nameof(wmi));

    public string ActionName => "wmi_query";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = ActionParameterReader.GetTargetOrParameter(action, "query", "wql");
        var wmiNamespace = ActionParameterReader.GetTargetOrParameter(action, "namespace", "wmiNamespace");
        return Task.FromResult(_wmi.Query(query ?? string.Empty, wmiNamespace));
    }
}

public sealed class ScheduleTaskActionHandler : IActionHandler
{
    private readonly ITaskSchedulerIntegrationService _scheduler;

    public ScheduleTaskActionHandler(ITaskSchedulerIntegrationService scheduler) =>
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

    public string ActionName => "schedule_task";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "create";
        var name = ActionParameterReader.GetTargetOrParameter(action, "name", "taskName");
        var command = ActionParameterReader.GetTargetOrParameter(action, "command", "exe");
        var trigger = ActionParameterReader.GetTargetOrParameter(action, "trigger");
        var arguments = ActionParameterReader.GetTargetOrParameter(action, "arguments", "args");

        return Task.FromResult(_scheduler.Execute(mode, name, command, trigger, arguments));
    }
}

public sealed class JumpListActionHandler : IActionHandler
{
    private readonly IJumpListService _jumpList;

    public JumpListActionHandler(IJumpListService jumpList) =>
        _jumpList = jumpList ?? throw new ArgumentNullException(nameof(jumpList));

    public string ActionName => "jump_list";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "set";
        var tasks = ActionParameterReader.GetTargetOrParameter(action, "tasks", "items");
        return await _jumpList.UpdateAsync(mode, tasks, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ComInvokeActionHandler : IActionHandler
{
    private readonly IComAutomationService _com;

    public ComInvokeActionHandler(IComAutomationService com) =>
        _com = com ?? throw new ArgumentNullException(nameof(com));

    public string ActionName => "com_invoke";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var progId = ActionParameterReader.GetTargetOrParameter(action, "progId", "progid");
        var method = ActionParameterReader.GetTargetOrParameter(action, "method");
        var arguments = ActionParameterReader.GetTargetOrParameter(action, "arguments", "args");
        var close = ActionParameterReader.GetTargetOrParameter(action, "close");
        var closeInstance = close is not null &&
                            (close.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                             close.Equals("1", StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(_com.Invoke(progId ?? string.Empty, method ?? string.Empty, arguments, closeInstance));
    }
}

public sealed class VerifyUserActionHandler : IActionHandler
{
    private readonly IWindowsHelloService _hello;

    public VerifyUserActionHandler(IWindowsHelloService hello) =>
        _hello = hello ?? throw new ArgumentNullException(nameof(hello));

    public string ActionName => "verify_user";

    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var message = ActionParameterReader.GetTargetOrParameter(action, "message", "reason")
                      ?? "Windows AI Assistant islem onayi";
        return await _hello.VerifyAsync(message, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class GlobalHookActionHandler : IActionHandler
{
    private readonly IGlobalHookService _hooks;

    public GlobalHookActionHandler(IGlobalHookService hooks) =>
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));

    public string ActionName => "global_hook";

    public Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mode = ActionParameterReader.GetTargetOrParameter(action, "mode") ?? "status";
        var hookType = ActionParameterReader.GetTargetOrParameter(action, "type", "hookType");
        var maxEvents = ActionParameterReader.TryGetInt(action, "maxEvents", out var max) ? max : 32;
        return Task.FromResult(_hooks.Execute(mode, hookType, maxEvents));
    }
}
