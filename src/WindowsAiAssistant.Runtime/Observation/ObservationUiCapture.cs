namespace WindowsAiAssistant.Runtime.Observation;

public static class ObservationUiCapture
{
    public const string SelfWindowSkipReason =
        "atlandi (Windows AI Assistant penceresi — bu UI otomatiklestirilmez; open_app/shell/focus_window kullanin)";

    public static bool ShouldSkipSelfWindow(string? activeProcessName, string? foregroundProcessName) =>
        AgentSelfWindow.IsAssistantProcess(activeProcessName) ||
        AgentSelfWindow.IsAssistantProcess(foregroundProcessName);

    public static string FormatTimeout(int timeoutMs) =>
        $"UIA zaman asimi ({timeoutMs} ms)";

    public static string FormatCaptureFailure(Exception ex)
    {
        var typeName = ex.GetType().Name;
        var message = SanitizeMessage(ex.Message);
        return $"UIA yakalama hatasi ({typeName}): {message}";
    }

    internal static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "bilinmeyen hata";
        }

        var oneLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 160 ? oneLine : oneLine[..160];
    }
}
