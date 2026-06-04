namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ObservationCaptureOptions
{
    public string? LastUserGoal { get; init; }
    public string? LastActionResult { get; init; }
    public string? RunId { get; init; }
    public int? StepIndex { get; init; }
}
