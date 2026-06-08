using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Debugging;
using WindowsAiAssistant.Runtime.Policy;
using WindowsAiAssistant.Runtime.Session;

namespace WindowsAiAssistant.App.Services;

public sealed class PendingApprovalRequest
{
    private int _completed;

    public PendingApprovalRequest(GateDecision gateDecision, AgentAction action)
    {
        GateDecision = gateDecision ?? throw new ArgumentNullException(nameof(gateDecision));
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public GateDecision GateDecision { get; }
    public AgentAction Action { get; }

    internal TaskCompletionSource<bool> CompletionSource { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> WaitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.Register(() => TryComplete(false));
        return CompletionSource.Task;
    }

    public void Approve(bool rememberForSession = false)
    {
        RememberForSession = rememberForSession;
        TryComplete(true);
    }

    public void Deny() => TryComplete(false);

    public bool RememberForSession { get; private set; }

    private void TryComplete(bool approved)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
        {
            return;
        }

        CompletionSource.TrySetResult(approved);
    }
}

public sealed class ActionApprovalCoordinator : IActionApprovalHandler
{
    private readonly ActionGate _actionGate;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private bool _overlayMode;

    public ActionApprovalCoordinator(ActionGate actionGate) =>
        _actionGate = actionGate ?? throw new ArgumentNullException(nameof(actionGate));

    public event EventHandler<PendingApprovalRequest>? ApprovalRequested;
    public event EventHandler<PendingApprovalRequest>? OverlayApprovalRequested;

    public void SetDispatcherQueue(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue) =>
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));

    public void SetOverlayMode(bool active) => _overlayMode = active;

    public async Task<bool> RequestApprovalAsync(
        GateDecision gateDecision,
        AgentAction action,
        CancellationToken cancellationToken = default)
    {
        var request = new PendingApprovalRequest(gateDecision, action);
        var dispatcher = _dispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
        {
            return false;
        }

        var enqueued = dispatcher.TryEnqueue(() =>
        {
            if (_overlayMode)
            {
                OverlayApprovalRequested?.Invoke(this, request);
            }
            else
            {
                ApprovalRequested?.Invoke(this, request);
            }
        });

        if (!enqueued)
        {
            return false;
        }

        var approved = await request.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (approved)
        {
            if (request.RememberForSession)
            {
                _actionGate.RememberSessionApproval(gateDecision.ApprovalKey);
            }

            if (action.Action.Equals("shell", StringComparison.OrdinalIgnoreCase))
            {
                var command = ActionParameterReader.GetTargetOrParameter(action, "command", "cmd", "script");
                DebugAgentLog.Write(
                    "F009",
                    "ActionApprovalCoordinator.RequestApprovalAsync",
                    "approved shell command",
                    new { command = ShellSecurityPolicy.SanitizeForAudit(command ?? string.Empty) },
                    AgentRunScope.Current?.RunId ?? _actionGate.ActiveRunId);
            }
        }

        return approved;
    }
}

public static class ActionRiskDisplay
{
    public static string ToUiLabel(ActionRisk risk) => risk switch
    {
        ActionRisk.Safe => "low",
        ActionRisk.Normal => "low",
        ActionRisk.Sensitive => "medium",
        ActionRisk.Destructive => "critical",
        _ => "medium"
    };
}
