using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.Tests;

public sealed class ComAllowlistValidatorTests
{
    private static readonly IReadOnlyList<ComAllowedOperation> DefaultList = ComAllowlistDefaults.CreateDefault();

    [Fact]
    public void IsAllowed_KnownOfficeOperation_ReturnsTrue()
    {
        Assert.True(ComAllowlistValidator.IsAllowed("Word.Application", "Documents.Add", DefaultList));
    }

    [Fact]
    public void IsAllowed_UnknownProgId_ReturnsFalse()
    {
        Assert.False(ComAllowlistValidator.IsAllowed("Malicious.App", "Run", DefaultList));
    }

    [Fact]
    public void IsAllowed_KnownProgUnknownMethod_ReturnsFalse()
    {
        Assert.False(ComAllowlistValidator.IsAllowed("Word.Application", "MacroSecurity.AllowAll", DefaultList));
    }

    [Fact]
    public void IsAllowed_EmptyAllowlist_ReturnsFalse()
    {
        Assert.False(ComAllowlistValidator.IsAllowed("Word.Application", "Documents.Add", []));
    }
}
