using WindowsAiAssistant.Runtime.Policy;

namespace WindowsAiAssistant.Tests;

public sealed class ShellSecurityPolicyTests
{
    [Theory]
    [InlineData("Remove-Item C:\\temp\\x")]
    [InlineData("format C:")]
    [InlineData("Invoke-WebRequest http://evil")]
    [InlineData("iex (Get-Content script.ps1)")]
    public void IsDestructive_KnownPatterns_ReturnsTrue(string command)
    {
        Assert.True(ShellSecurityPolicy.IsDestructive(command));
    }

    [Theory]
    [InlineData("Get-Process")]
    [InlineData("dir C:\\Users")]
    [InlineData("echo hello")]
    public void IsDestructive_SafeCommands_ReturnsFalse(string command)
    {
        Assert.False(ShellSecurityPolicy.IsDestructive(command));
    }

    [Fact]
    public void SanitizeForAudit_TruncatesLongCommands()
    {
        var longCommand = new string('a', 600);
        var sanitized = ShellSecurityPolicy.SanitizeForAudit(longCommand, maxChars: 100);

        Assert.True(sanitized.Length < longCommand.Length);
        Assert.Contains("karakter", sanitized, StringComparison.Ordinal);
    }
}
