namespace WindowsAiAssistant.Runtime.Observation;

public sealed record ForegroundWindowDetails(
    string WindowTitle,
    string ProcessName,
    int ProcessId,
    string WindowClassName);
