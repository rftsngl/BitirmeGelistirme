using System.Text;
using WindowsAiAssistant.Agent.Dispatch;
using WindowsAiAssistant.Runtime.Observation;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Tests;

public sealed class OfficeWorkflowPlaybookTests
{
    [Fact]
    public void AppendIfRelevant_WordAlreadyOpen_SuggestsFocusNotOpenApp()
    {
        var builder = new StringBuilder();
        var observation = new DesktopObservation
        {
            Windows =
            [
                new WindowInfo
                {
                    WindowId = "w3",
                    Handle = 0,
                    Title = "Word",
                    ProcessName = "WINWORD"
                }
            ]
        };

        OfficeWorkflowPlaybook.AppendIfRelevant(
            builder,
            "Word aç. sonra boş belgeyi seç. Keloğlan masalları başlığı oluştur.",
            observation);

        var text = builder.ToString();
        Assert.Contains("WORD / OFFICE WORKFLOW", text, StringComparison.Ordinal);
        Assert.Contains("open_app KULLANMA", text, StringComparison.Ordinal);
        Assert.Contains("focus_window", text, StringComparison.Ordinal);
        Assert.Contains("Documents.Add", text, StringComparison.Ordinal);
        Assert.Contains("ikinci kez open_app", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendIfRelevant_UnrelatedGoal_DoesNotAppend()
    {
        var builder = new StringBuilder();
        OfficeWorkflowPlaybook.AppendIfRelevant(builder, "sesi kapat", new DesktopObservation());
        Assert.Equal(string.Empty, builder.ToString());
    }
}
