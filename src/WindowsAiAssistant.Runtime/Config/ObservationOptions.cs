namespace WindowsAiAssistant.Runtime.Config;

public sealed class ObservationOptions
{
    /// <summary>
    /// Statik ekranda önceki snapshot yeniden kullanımı. Kapalıyken her adımda tam capture.
    /// </summary>
    public bool EnableIncrementalReuse { get; set; } = true;

    /// <summary>
    /// Her N adımda bir tam capture zorlanır (1 = her adım tam capture).
    /// </summary>
    public int FullCaptureEveryNSteps { get; set; } = 1;
}
