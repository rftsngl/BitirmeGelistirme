using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Agent.Dispatch;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Observation;
using WindowsAiAssistant.Runtime.Policy;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Tests;

public sealed class OrchestrationIntegrationTests
{
    private readonly DecisionParser _parser = new();
    private readonly TaskDispatchRouter _router = new();

    private static ActionGate CreateGate() =>
        new(
            new ActionPolicy(),
            new ForegroundWindowService(),
            new ForegroundFocusService(new WindowManager()));

    [Fact]
    public void FastRoute_ParseAndGate_AllowsMuteGoal()
    {
        var routed = _router.TryResolve("sesi kapat", observation: null);
        Assert.NotNull(routed);

        const string json = """
            {
              "decisionType": "execute_action",
              "action": "audio_power",
              "parameters": { "mode": "mute" },
              "isComplete": false
            }
            """;

        var parsed = _parser.Parse(json);
        Assert.True(parsed.Success);
        Assert.Equal("audio_power", parsed.Decision!.Action);

        var gate = CreateGate();
        var decision = gate.Evaluate(parsed.Decision.ToAgentAction());
        Assert.Equal(GateOutcome.Allow, decision.Outcome);
    }

    [Fact]
    public void SelectText_ParseAndGate_AllowsAllMode()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "select_text",
              "parameters": { "mode": "all" },
              "isComplete": false
            }
            """;

        var parsed = _parser.Parse(json);
        Assert.True(parsed.Success);

        var gate = CreateGate();
        var decision = gate.Evaluate(parsed.Decision!.ToAgentAction());
        Assert.Equal(GateOutcome.Allow, decision.Outcome);
        Assert.Equal(ActionRisk.Normal, decision.Risk);
    }

    [Fact]
    public void SelectAllFastRoute_MatchesCtrlA()
    {
        var routed = _router.TryResolve("hepsini sec", observation: null);

        Assert.NotNull(routed);
        Assert.Equal("press_shortcut", routed!.Action);
        Assert.Equal("Ctrl+A", routed.Target);
    }
}
