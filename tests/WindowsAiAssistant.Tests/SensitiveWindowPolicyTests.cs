using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.Tests;

public sealed class SensitiveWindowPolicyTests
{
    [Theory]
    [InlineData("Windows Security", "svchost", "")]
    [InlineData("Kullanıcı Hesabı Denetimi", "consent", "")]
    [InlineData("Sign in", "CredentialUIBroker", "Credential Dialog Xaml Host")]
    public void ShouldSkipScreenshot_SensitiveSignals_ReturnsTrue(
        string title,
        string process,
        string windowClass)
    {
        Assert.True(SensitiveWindowPolicy.ShouldSkipScreenshot(title, process, windowClass));
    }

    [Fact]
    public void ShouldSkipScreenshot_AssistantWindow_ReturnsTrue()
    {
        Assert.True(SensitiveWindowPolicy.ShouldSkipScreenshot(
            "Windows AI Assistant",
            "WindowsAiAssistant.App",
            "WinUIDesktopWin32WindowClass"));
    }

    [Theory]
    [InlineData("Notepad", "notepad", "Notepad")]
    [InlineData("Google Chrome", "chrome", "Chrome_WidgetWin_1")]
    public void ShouldSkipScreenshot_NormalApp_ReturnsFalse(
        string title,
        string process,
        string windowClass)
    {
        Assert.False(SensitiveWindowPolicy.ShouldSkipScreenshot(title, process, windowClass));
    }
}
