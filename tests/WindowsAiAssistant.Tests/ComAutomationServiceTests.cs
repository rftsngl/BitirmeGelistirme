using WindowsAiAssistant.Runtime.Config;
using WindowsAiAssistant.Runtime.Integrations;

namespace WindowsAiAssistant.Tests;

public sealed class ComAutomationServiceTests
{
    [Fact]
    public void Invoke_AllowlistDenied_DoesNotAttemptCom()
    {
        var service = new ComAutomationService(new RuntimeOptions
        {
            ComAutomation = new ComAutomationOptions
            {
                AllowedOperations =
                [
                    new() { ProgId = "Word.Application", Methods = ["Documents.Add"] }
                ]
            }
        });

        var result = service.Invoke("Excel.Application", "Workbooks.Add");

        Assert.False(result.Success);
        Assert.Contains("allowlist", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
