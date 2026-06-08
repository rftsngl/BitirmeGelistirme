namespace WindowsAiAssistant.Agent;

public static class VisionAttachmentPolicy
{
    public static string? SelectImagePayload(
        bool visionEnabled,
        string? base64Png,
        string? screenshotSkipReason = null) =>
        visionEnabled &&
        string.IsNullOrWhiteSpace(screenshotSkipReason) &&
        !string.IsNullOrWhiteSpace(base64Png)
            ? base64Png
            : null;
}
