using WindowsAiAssistant.Agent.Planning;

namespace WindowsAiAssistant.Tests;

public sealed class ExecutionPlanParserTests
{
    [Fact]
    public void Parse_ValidPlan_ReturnsSteps()
    {
        const string json = """
            {
              "goalType": "desktop_multi",
              "summary": "Word ac ve baslik yaz",
              "preconditions": ["visibleWindows kontrol et"],
              "steps": [
                {
                  "order": 1,
                  "intent": "Word ac veya odaklan",
                  "preferredActions": ["focus_window", "open_app"],
                  "successCheck": "WINWORD odakta"
                },
                {
                  "order": 2,
                  "intent": "Bos belge",
                  "preferredActions": ["com_invoke", "press_shortcut Ctrl+N"],
                  "successCheck": "belge penceresi acik"
                }
              ],
              "antiPatterns": ["ikinci open_app yok"],
              "estimatedSteps": 3
            }
            """;

        var result = new ExecutionPlanParser().Parse(json);

        Assert.True(result.Success);
        Assert.NotNull(result.Plan);
        Assert.Equal(2, result.Plan!.Steps.Count);
        Assert.Contains("ikinci open_app", result.Plan.AntiPatterns[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ConversationGoal_Fails()
    {
        const string json = """
            {
              "goalType": "conversation",
              "summary": "sohbet",
              "steps": [{"order": 1, "intent": "yanitla"}]
            }
            """;

        var result = new ExecutionPlanParser().Parse(json);
        Assert.False(result.Success);
    }
}
