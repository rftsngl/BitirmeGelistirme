namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ObservationCaptureOptions
{
    public string? LastUserGoal { get; init; }
    public string? LastActionResult { get; init; }
    public string? RunId { get; init; }
    public int? StepIndex { get; init; }

    /// <summary>Bir onceki adimda aktif olan pencere basligi (action sonrasi diff icin).</summary>
    public string? PreviousActiveWindowTitle { get; init; }

    /// <summary>Bir onceki adimda aktif olan surec adi (action sonrasi diff icin).</summary>
    public string? PreviousActiveProcessName { get; init; }

    /// <summary>Incremental observation icin onceki tam snapshot.</summary>
    public DesktopObservation? PreviousObservation { get; init; }
}
