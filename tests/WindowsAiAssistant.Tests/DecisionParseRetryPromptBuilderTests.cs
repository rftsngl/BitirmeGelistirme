using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Tests;

public sealed class DecisionParseRetryPromptBuilderTests
{
    [Fact]
    public void Build_InvalidElementId_IncludesElementIdSamples()
    {
        var elements = new List<UiElementSnapshot>
        {
            new() { ElementId = "btn-save-a1b2", Name = "Kaydet", ControlType = "Button", IsEnabled = true }
        };

        var failed = DecisionParseResult.Fail(
            "click_element icin gecerli elementId gerekli.",
            DecisionParseErrorCode.InvalidElementId);

        var prompt = DecisionParseRetryPromptBuilder.Build("base prompt", failed, elements);

        Assert.Contains("btn-save-a1b2", prompt, StringComparison.Ordinal);
        Assert.Contains("elementId", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_InvalidJson_IncludesSchemaExample()
    {
        var failed = DecisionParseResult.Fail(
            "Karar JSON formatinda degil.",
            DecisionParseErrorCode.InvalidJson);

        var prompt = DecisionParseRetryPromptBuilder.Build("base prompt", failed, uiElements: null);

        Assert.Contains("decisionType", prompt, StringComparison.Ordinal);
        Assert.Contains("execute_action", prompt, StringComparison.Ordinal);
        Assert.Contains("no markdown", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_InvalidElementId_WindowGoal_SuggestsListWindows()
    {
        var failed = DecisionParseResult.Fail(
            "click_element icin gecerli elementId gerekli.",
            DecisionParseErrorCode.InvalidElementId);

        var prompt = DecisionParseRetryPromptBuilder.Build(
            "base prompt",
            failed,
            uiElements: null,
            userGoal: "acik pencereleri ozetle");

        Assert.Contains("list_windows", prompt, StringComparison.Ordinal);
        Assert.Contains("STRATEGY CHANGE", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_MissingDecisionType_MentionsDecisionTypeField()
    {
        var failed = DecisionParseResult.Fail(
            "Kararda 'decisionType' alani eksik.",
            DecisionParseErrorCode.MissingDecisionType);

        var prompt = DecisionParseRetryPromptBuilder.Build("base prompt", failed, uiElements: null);

        Assert.Contains("decisionType", prompt, StringComparison.Ordinal);
        Assert.Contains("legacy field 'type'", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
