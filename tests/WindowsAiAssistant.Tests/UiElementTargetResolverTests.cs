using WindowsAiAssistant.Runtime.Automation;

namespace WindowsAiAssistant.Tests;

public sealed class UiElementTargetResolverTests
{
    private static readonly IReadOnlyList<UiElementSnapshot> SampleElements =
    [
        new()
        {
            ElementId = "btn-yeni-a1b2",
            ControlType = "Button",
            Name = "Yeni",
            IsEnabled = true
        },
        new()
        {
            ElementId = "btn-kaydet-b3c4",
            ControlType = "Button",
            Name = "Kaydet",
            IsEnabled = true
        }
    ];

    [Fact]
    public void TryResolve_ExactName_ReturnsElementId()
    {
        var resolved = UiElementTargetResolver.TryResolve("Yeni", SampleElements);
        Assert.Equal("btn-yeni-a1b2", resolved);
    }

    [Fact]
    public void TryResolve_ValidElementId_ReturnsSameId()
    {
        var resolved = UiElementTargetResolver.TryResolve("btn-kaydet-b3c4", SampleElements);
        Assert.Equal("btn-kaydet-b3c4", resolved);
    }

    [Fact]
    public void TryResolve_UnknownLabel_ReturnsNull()
    {
        var resolved = UiElementTargetResolver.TryResolve("Bilinmeyen", SampleElements);
        Assert.Null(resolved);
    }

    [Fact]
    public void TryResolve_PartialNameMatch_ReturnsUniqueElementId()
    {
        var resolved = UiElementTargetResolver.TryResolve("Kay", SampleElements);
        Assert.Equal("btn-kaydet-b3c4", resolved);
    }
}
