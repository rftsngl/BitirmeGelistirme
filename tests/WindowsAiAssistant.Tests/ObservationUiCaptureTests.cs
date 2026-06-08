using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Tests;

public sealed class ObservationUiCaptureTests
{
    [Fact]
    public void ShouldSkipSelfWindow_AssistantProcess_ReturnsTrue()
    {
        Assert.True(ObservationUiCapture.ShouldSkipSelfWindow("WindowsAiAssistant.App", "notepad"));
        Assert.True(ObservationUiCapture.ShouldSkipSelfWindow("notepad", "WindowsAiAssistant"));
    }

    [Fact]
    public void ShouldSkipSelfWindow_ExternalProcess_ReturnsFalse()
    {
        Assert.False(ObservationUiCapture.ShouldSkipSelfWindow("notepad", "chrome"));
    }

    [Fact]
    public void SelfWindowSkipReason_IsDocumentedConstant()
    {
        Assert.Contains("Windows AI Assistant", ObservationUiCapture.SelfWindowSkipReason, StringComparison.Ordinal);
        Assert.Contains("focus_window", ObservationUiCapture.SelfWindowSkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCaptureFailure_IncludesExceptionTypeAndSanitizedMessage()
    {
        var reason = ObservationUiCapture.FormatCaptureFailure(new InvalidOperationException("UIA tree failed\nline2"));

        Assert.Contains("InvalidOperationException", reason, StringComparison.Ordinal);
        Assert.Contains("UIA tree failed", reason, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', reason);
    }

    [Fact]
    public void FormatTimeout_IncludesMilliseconds()
    {
        Assert.Equal("UIA zaman asimi (8000 ms)", ObservationUiCapture.FormatTimeout(8000));
    }
}
