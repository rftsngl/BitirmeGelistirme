using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Tests;

public sealed class VisionAttachmentPolicyTests
{
    [Fact]
    public void SelectImagePayload_VisionDisabled_ReturnsNull()
    {
        Assert.Null(VisionAttachmentPolicy.SelectImagePayload(false, "abc123"));
    }

    [Fact]
    public void SelectImagePayload_VisionEnabledWithoutImage_ReturnsNull()
    {
        Assert.Null(VisionAttachmentPolicy.SelectImagePayload(true, null));
        Assert.Null(VisionAttachmentPolicy.SelectImagePayload(true, "   "));
    }

    [Fact]
    public void SelectImagePayload_VisionEnabledWithImage_ReturnsPayload()
    {
        const string payload = "base64png";
        Assert.Equal(payload, VisionAttachmentPolicy.SelectImagePayload(true, payload));
    }

    [Fact]
    public void SelectImagePayload_SkipReason_ReturnsNullEvenWhenVisionEnabled()
    {
        Assert.Null(VisionAttachmentPolicy.SelectImagePayload(
            true,
            "base64png",
            SensitiveWindowPolicy.ScreenshotSkipReason));
    }
}
