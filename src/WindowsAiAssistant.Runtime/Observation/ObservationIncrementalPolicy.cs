namespace WindowsAiAssistant.Runtime.Observation;

public static class ObservationIncrementalPolicy
{
    public static bool ShouldReuse(
        bool enableIncremental,
        int fullCaptureEveryNSteps,
        int stepIndex,
        string? lastActionResult,
        string currentFingerprint,
        string? previousFingerprint)
    {
        if (!enableIncremental || string.IsNullOrWhiteSpace(previousFingerprint))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(lastActionResult))
        {
            return false;
        }

        var interval = Math.Max(1, fullCaptureEveryNSteps);
        if (stepIndex % interval == 0)
        {
            return false;
        }

        return string.Equals(currentFingerprint, previousFingerprint, StringComparison.Ordinal);
    }
}
