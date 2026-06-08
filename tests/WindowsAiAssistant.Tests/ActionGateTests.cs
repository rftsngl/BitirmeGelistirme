using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Observation;
using WindowsAiAssistant.Runtime.Policy;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Tests;

public sealed class ActionGateTests
{
    private static ActionGate CreateGate(ActionPolicy? policy = null) =>
        new(
            policy ?? new ActionPolicy(),
            new ForegroundWindowService(),
            new ForegroundFocusService(new WindowManager()));

    private static AgentAction Action(string name, string? target = null, Dictionary<string, string>? parameters = null) =>
        new()
        {
            Action = name,
            Target = target,
            Parameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

    [Fact]
    public void Evaluate_SafeAction_AllowsAutomatically()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("audio_power", parameters: new() { ["mode"] = "mute" }));

        Assert.Equal(GateOutcome.Allow, decision.Outcome);
        Assert.Equal(ActionRisk.Safe, decision.Risk);
    }

    [Fact]
    public void Evaluate_NetworkStatus_IsSafeAndAllowed()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("network_status", parameters: new() { ["mode"] = "status" }));

        Assert.Equal(GateOutcome.Allow, decision.Outcome);
        Assert.Equal(ActionRisk.Safe, decision.Risk);
    }

    [Fact]
    public void Evaluate_OpenApp_IsNormalAndAllowedByDefaultPolicy()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("open_app", "notepad"));

        Assert.Equal(GateOutcome.Allow, decision.Outcome);
        Assert.Equal(ActionRisk.Normal, decision.Risk);
    }

    [Fact]
    public void Evaluate_PressShortcut_IsSensitiveAndRequiresApproval()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("press_shortcut", "Alt+F4"));

        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
        Assert.Equal(ActionRisk.Sensitive, decision.Risk);
    }

    [Fact]
    public void Evaluate_KnownSafeShortcut_IsNormalAndAllowed()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("press_shortcut", "Ctrl+S"));

        Assert.Equal(GateOutcome.Allow, decision.Outcome);
        Assert.Equal(ActionRisk.Normal, decision.Risk);
    }

    [Fact]
    public void Evaluate_ControlAliasShortcut_IsNormalAndAllowed()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("press_shortcut", "Control+N"));

        Assert.Equal(GateOutcome.Allow, decision.Outcome);
        Assert.Equal(ActionRisk.Normal, decision.Risk);
    }

    [Fact]
    public void Evaluate_UnknownShortcut_RemainsSensitive()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("press_shortcut", "Ctrl+Shift+Alt+Q"));

        Assert.Equal(ActionRisk.Sensitive, decision.Risk);
        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
    }

    [Fact]
    public void Evaluate_DestructiveShell_RequiresApproval()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("shell", parameters: new() { ["command"] = "del C:\\temp\\file.txt" }));

        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
        Assert.Equal(ActionRisk.Destructive, decision.Risk);
    }

    [Fact]
    public void Evaluate_NonDestructiveShell_IsSensitive()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("shell", parameters: new() { ["command"] = "Get-Process" }));

        Assert.Equal(ActionRisk.Sensitive, decision.Risk);
        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
    }

    [Fact]
    public void Evaluate_WindowClose_IsDestructive()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action(
            "window_state",
            "Notepad",
            new() { ["state"] = "close" }));

        Assert.Equal(ActionRisk.Destructive, decision.Risk);
        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
    }

    [Fact]
    public void Evaluate_UnknownAction_IsDenied()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("unknown_action_xyz"));

        Assert.Equal(GateOutcome.Deny, decision.Outcome);
    }

    [Fact]
    public void Evaluate_ComInvoke_IsSensitive()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action(
            "com_invoke",
            parameters: new()
            {
                ["progId"] = "Word.Application",
                ["method"] = "Documents.Add"
            }));

        Assert.Equal(ActionRisk.Sensitive, decision.Risk);
        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
    }

    [Fact]
    public void Evaluate_SensitiveDeniedByPolicy_IsDenied()
    {
        var policy = new ActionPolicy { Sensitive = RiskHandling.Deny };
        var gate = CreateGate(policy);
        var decision = gate.Evaluate(Action("press_key", "F12"));

        Assert.Equal(GateOutcome.Deny, decision.Outcome);
        Assert.Equal(ActionRisk.Sensitive, decision.Risk);
    }

    [Fact]
    public void RememberSessionApproval_AllowsPreviouslyApprovedDestructiveShell()
    {
        var gate = CreateGate(new ActionPolicy { AllowSessionRemember = true });
        var action = Action("shell", parameters: new() { ["command"] = "Remove-Item C:\\temp\\x" });

        var first = gate.Evaluate(action);
        Assert.Equal(GateOutcome.RequireApproval, first.Outcome);

        gate.RememberSessionApproval(first.ApprovalKey);
        var second = gate.Evaluate(action);

        Assert.Equal(GateOutcome.Allow, second.Outcome);
        Assert.Equal(ActionRisk.Destructive, second.Risk);
    }

    [Fact]
    public void BeginSession_ClearsRememberedApprovals()
    {
        var gate = CreateGate(new ActionPolicy { AllowSessionRemember = true });
        var action = Action("shell", parameters: new() { ["command"] = "format C:" });

        var first = gate.Evaluate(action);
        gate.RememberSessionApproval(first.ApprovalKey);
        gate.BeginSession("run-2");

        var afterReset = gate.Evaluate(action);

        Assert.Equal(GateOutcome.RequireApproval, afterReset.Outcome);
        Assert.Equal("run-2", gate.ActiveRunId);
    }

    [Fact]
    public void Evaluate_InvokeWebRequestShell_IsDestructive()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action("shell", parameters: new() { ["command"] = "Invoke-WebRequest https://x" }));

        Assert.Equal(ActionRisk.Destructive, decision.Risk);
    }

    [Fact]
    public void Evaluate_MouseClickWithCoordinates_IsSensitive()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action(
            "mouse_click",
            parameters: new() { ["x"] = "100", ["y"] = "200" }));

        Assert.Equal(ActionRisk.Sensitive, decision.Risk);
    }

    [Fact]
    public void Evaluate_MouseClickWithElementId_IsNormal()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action(
            "mouse_click",
            parameters: new() { ["elementId"] = "btn-save-c0de" }));

        Assert.Equal(ActionRisk.Normal, decision.Risk);
        Assert.Equal(GateOutcome.Allow, decision.Outcome);
    }

    [Fact]
    public void Evaluate_MouseClickRightButton_IsSensitive()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action(
            "mouse_click",
            parameters: new() { ["elementId"] = "btn-save-c0de", ["button"] = "right" }));

        Assert.Equal(ActionRisk.Sensitive, decision.Risk);
        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
    }

    [Fact]
    public void Evaluate_MouseMove_IsSensitive()
    {
        var gate = CreateGate();
        var decision = gate.Evaluate(Action(
            "mouse_move",
            parameters: new() { ["x"] = "10", ["y"] = "20" }));

        Assert.Equal(ActionRisk.Sensitive, decision.Risk);
        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
    }
}
