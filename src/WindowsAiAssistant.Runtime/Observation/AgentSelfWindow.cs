namespace WindowsAiAssistant.Runtime.Observation;

internal static class AgentSelfWindow
{
    public static bool IsAssistantProcess(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) &&
        processName.Contains("WindowsAiAssistant", StringComparison.OrdinalIgnoreCase);
}
