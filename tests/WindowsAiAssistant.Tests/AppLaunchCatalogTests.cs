namespace WindowsAiAssistant.Tests;

public sealed class AppLaunchCatalogTests
{
    [Theory]
    [InlineData("word", "WINWORD")]
    [InlineData("excel", "EXCEL")]
    [InlineData("chrome", "chrome")]
    public void TryResolveProcessNamesForOpenApp_KnownApps_ReturnsProcessName(string target, string expectedProcess)
    {
        var found = WindowsAiAssistant.Runtime.Actions.AppLaunchCatalog.TryResolveProcessNamesForOpenApp(
            target,
            out var processNames);

        Assert.True(found);
        Assert.Contains(expectedProcess, processNames, StringComparer.OrdinalIgnoreCase);
    }
}
