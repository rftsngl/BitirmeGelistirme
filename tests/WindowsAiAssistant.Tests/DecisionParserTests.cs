using WindowsAiAssistant.Agent;

namespace WindowsAiAssistant.Tests;

public sealed class DecisionParserTests
{
    private readonly DecisionParser _parser = new();

    [Fact]
    public void Parse_EmptyInput_Fails()
    {
        var result = _parser.Parse("   ");

        Assert.False(result.Success);
        Assert.Contains("bos", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NonJsonText_Fails()
    {
        var result = _parser.Parse("Merhaba, su anda bir karar yok.");

        Assert.False(result.Success);
        Assert.Contains("JSON", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InvalidJson_Fails()
    {
        var result = _parser.Parse("{ decisionType: broken }");

        Assert.False(result.Success);
        Assert.Contains("JSON", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingDecisionType_Fails()
    {
        const string json = """
            {
              "action": "respond",
              "parameters": { "message": "Tamam" }
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("decisionType", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_LegacyTypeField_Fails()
    {
        const string json = """
            {
              "type": "execute_action",
              "action": "wait",
              "parameters": {}
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("decisionType", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_UnsupportedAction_Fails()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "fly_to_moon",
              "parameters": {}
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("Desteklenmeyen", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RespondWithoutMessage_Fails()
    {
        const string json = """
            {
              "decisionType": "complete",
              "action": "respond",
              "parameters": {},
              "isComplete": true
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("message", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ValidRespond_Succeeds()
    {
        const string json = """
            {
              "decisionType": "complete",
              "action": "respond",
              "parameters": { "message": "Gorev tamamlandi." },
              "isComplete": true
            }
            """;

        var result = _parser.Parse(json);

        Assert.True(result.Success);
        Assert.NotNull(result.Decision);
        Assert.Equal("respond", result.Decision.Action);
        Assert.Equal("Gorev tamamlandi.", result.Decision.Parameters["message"]);
        Assert.True(result.Decision.IsComplete);
    }

    [Fact]
    public void Parse_ValidOpenApp_Succeeds()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "open_app",
              "target": "notepad",
              "parameters": {},
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json);

        Assert.True(result.Success);
        Assert.Equal("notepad", result.Decision!.Target);
    }

    [Fact]
    public void Parse_ClickElementInvalidElementId_Fails()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "click_element",
              "target": "not-a-valid-id",
              "parameters": {},
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("elementId", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MarkdownFencedJson_Succeeds()
    {
        const string json = """
            ```json
            {
              "decisionType": "execute_action",
              "action": "wait",
              "parameters": {},
              "isComplete": false
            }
            ```
            """;

        var result = _parser.Parse(json);

        Assert.True(result.Success);
        Assert.Equal("wait", result.Decision!.Action);
    }

    [Fact]
    public void Parse_AskUserWrongAction_Fails()
    {
        const string json = """
            {
              "decisionType": "ask_user",
              "action": "respond",
              "parameters": { "message": "Ne yapmamı istersin?" },
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("ask_user", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ShellWithoutCommand_Fails()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "shell",
              "parameters": {},
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("shell", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MouseMoveWithoutCoordinates_Fails()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "mouse_move",
              "parameters": { "x": "10" },
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("mouse_move", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MouseClickInvalidButton_Fails()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "mouse_click",
              "parameters": { "x": "1", "y": "2", "button": "double" },
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Success);
        Assert.Contains("button", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ValidMouseMove_Succeeds()
    {
        const string json = """
            {
              "decisionType": "execute_action",
              "action": "mouse_move",
              "parameters": { "x": "10", "y": "20" },
              "isComplete": false
            }
            """;

        var result = _parser.Parse(json);

        Assert.True(result.Success);
        Assert.Equal("mouse_move", result.Decision!.Action);
    }
}
