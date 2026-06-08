using System.Text;
using WindowsAiAssistant.Agent.Dispatch;
using WindowsAiAssistant.Runtime.Observation;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Tests;

public sealed class FastRoutePromptHintsTests
{
    [Fact]
    public void Append_MuteGoal_SuggestsRouteWithoutMandatory()
    {
        var builder = new StringBuilder();
        var observation = new DesktopObservation();

        FastRoutePromptHints.Append(builder, "sesi kapat", observation);

        var text = builder.ToString();
        Assert.Contains("SUGGESTED ROUTE", text, StringComparison.Ordinal);
        Assert.Contains("audio_power", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MANDATORY", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Append_WindowSummaryGoal_SuggestsListWindows()
    {
        var builder = new StringBuilder();
        var observation = new DesktopObservation
        {
            Windows =
            [
                new WindowInfo
                {
                    WindowId = "w1",
                    Handle = 0,
                    Title = "Notepad",
                    ProcessName = "notepad"
                }
            ]
        };

        FastRoutePromptHints.Append(builder, "acik pencereleri ozetle", observation);

        var text = builder.ToString();
        Assert.Contains("WINDOW LISTING GOAL", text, StringComparison.Ordinal);
        Assert.Contains("list_windows", text, StringComparison.Ordinal);
        Assert.Contains("visibleWindows", text, StringComparison.Ordinal);
    }
}
