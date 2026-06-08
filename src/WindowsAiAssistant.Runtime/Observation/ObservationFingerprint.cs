namespace WindowsAiAssistant.Runtime.Observation;

public static class ObservationFingerprint
{
    public static string Compute(
        string windowTitle,
        string processName,
        int processId,
        int cursorX,
        int cursorY,
        int visibleWindowCount) =>
        $"{windowTitle}|{processName}|{processId}|{cursorX}|{cursorY}|{visibleWindowCount}";
}
