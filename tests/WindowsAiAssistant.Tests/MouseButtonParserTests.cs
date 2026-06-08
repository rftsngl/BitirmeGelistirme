using WindowsAiAssistant.Runtime.Input;

namespace WindowsAiAssistant.Tests;

public sealed class MouseButtonParserTests
{
    [Theory]
    [InlineData(null, MouseButton.Left)]
    [InlineData("", MouseButton.Left)]
    [InlineData("left", MouseButton.Left)]
    [InlineData("RIGHT", MouseButton.Right)]
    [InlineData("middle", MouseButton.Middle)]
    [InlineData("mid", MouseButton.Middle)]
    public void TryParse_ValidValues_ReturnsTrue(string? value, MouseButton expected)
    {
        var ok = MouseButtonParser.TryParse(value, out var button);

        Assert.True(ok);
        Assert.Equal(expected, button);
    }

    [Theory]
    [InlineData("double")]
    [InlineData("wheel")]
    public void TryParse_InvalidValues_ReturnsFalse(string value)
    {
        var ok = MouseButtonParser.TryParse(value, out _);

        Assert.False(ok);
    }
}
