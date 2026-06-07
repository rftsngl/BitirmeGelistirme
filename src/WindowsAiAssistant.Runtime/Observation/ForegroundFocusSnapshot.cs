namespace WindowsAiAssistant.Runtime.Observation;

public sealed record ForegroundFocusSnapshot(nint Handle, string WindowTitle, string ProcessName);
