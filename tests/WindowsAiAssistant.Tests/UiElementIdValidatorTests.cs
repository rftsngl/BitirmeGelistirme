using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Tests;

public sealed class UiElementIdValidatorTests
{
    [Theory]
    [InlineData("btn-yeni-a1b2")]
    [InlineData("txt-search-1a2b")]
    [InlineData("chk-enable-ff00")]
    [InlineData("el-custom_item-9abc")]
    public void IsValidFormat_KnownGoodIds_ReturnsTrue(string elementId)
    {
        Assert.True(UiElementIdValidator.IsValidFormat(elementId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Yeni")]
    [InlineData("btn-yeni")]
    [InlineData("btn-yeni-a1b2-extra")]
    [InlineData("unknown-yeni-a1b2")]
    [InlineData("btn-yeni-zzzz")]
    public void IsValidFormat_InvalidIds_ReturnsFalse(string? elementId)
    {
        Assert.False(UiElementIdValidator.IsValidFormat(elementId));
    }

    [Theory]
    [InlineData("click_element", true)]
    [InlineData("focus_element", true)]
    [InlineData("read_element", true)]
    [InlineData("open_app", false)]
    [InlineData("shell", false)]
    public void IsUiAutomationAction_MatchesExpected(string action, bool expected)
    {
        Assert.Equal(expected, UiElementIdValidator.IsUiAutomationAction(action));
    }

    [Fact]
    public void GetElementTarget_PrefersTargetOverParameters()
    {
        var parameters = new Dictionary<string, string> { ["elementId"] = "btn-other-b3c4" };

        var resolved = UiElementIdValidator.GetElementTarget("click_element", "btn-yeni-a1b2", parameters);

        Assert.Equal("btn-yeni-a1b2", resolved);
    }

    [Fact]
    public void GetElementTarget_FallsBackToParameters()
    {
        var parameters = new Dictionary<string, string> { ["elementId"] = "btn-kaydet-b3c4" };

        var resolved = UiElementIdValidator.GetElementTarget("click_element", null, parameters);

        Assert.Equal("btn-kaydet-b3c4", resolved);
    }

    [Fact]
    public void GetElementTarget_MouseClickUsesElementIdParameter()
    {
        var parameters = new Dictionary<string, string> { ["elementId"] = "btn-save-c0de" };

        var resolved = UiElementIdValidator.GetElementTarget("mouse_click", null, parameters);

        Assert.Equal("btn-save-c0de", resolved);
    }
}
