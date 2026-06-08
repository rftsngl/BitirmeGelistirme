using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Tests;

public sealed class DecisionParserElementIdRepairTests
{
    private static readonly IReadOnlyList<UiElementSnapshot> WordRibbonElements =
    [
        new()
        {
            ElementId = "btn-yeni-a1b2",
            ControlType = "Button",
            Name = "Yeni",
            IsEnabled = true
        }
    ];

    private readonly DecisionParser _parser = new();

    [Fact]
    public void Parse_ClickElementWithVisibleLabel_RepairsToElementId()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "click_element",
              "target": "Yeni",
              "parameters": {},
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json, WordRibbonElements);

        Assert.True(result.Success);
        Assert.NotNull(result.Decision);
        Assert.Equal("btn-yeni-a1b2", result.Decision.Target);
        Assert.True(result.Decision.RepairedElementTarget);
    }

    [Fact]
    public void Parse_ClickElementWithValidElementId_SucceedsWithoutRepair()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "click_element",
              "target": "btn-yeni-a1b2",
              "parameters": {},
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json, WordRibbonElements);

        Assert.True(result.Success);
        Assert.NotNull(result.Decision);
        Assert.Equal("btn-yeni-a1b2", result.Decision.Target);
        Assert.False(result.Decision.RepairedElementTarget);
    }

    [Fact]
    public void Parse_ClickElementWithUnknownLabel_FailsWithElementIdHints()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "click_element",
              "target": "Sil",
              "parameters": {},
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json, WordRibbonElements);

        Assert.False(result.Success);
        Assert.Contains("btn-yeni-a1b2", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("elementId", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ClickElementWithoutUiElements_FailsForVisibleLabel()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "click_element",
              "target": "Yeni",
              "parameters": {},
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("uiElements listesi bos", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
