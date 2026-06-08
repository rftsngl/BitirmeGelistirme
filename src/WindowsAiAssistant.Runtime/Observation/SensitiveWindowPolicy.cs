namespace WindowsAiAssistant.Runtime.Observation;

public static class SensitiveWindowPolicy
{
    public const string ScreenshotSkipReason =
        "atlandi (hassas pencere — ekran goruntusu gizlilik icin alinmadi)";

    private static readonly string[] SensitiveProcessNames =
    [
        "CredentialUIBroker",
        "LogonUI",
        "LockApp",
        "consent",
        "WUDFHost"
    ];

    private static readonly string[] SensitiveWindowClasses =
    [
        "Credential Dialog Xaml Host",
        "Windows.UI.Core.CoreWindow",
        "#32769"
    ];

    private static readonly string[] SensitiveTitleKeywords =
    [
        "windows security",
        "guvenlik",
        "güvenlik",
        "credential",
        "kimlik bilgisi",
        "password",
        "sifre",
        "şifre",
        "pin kodu",
        "sign in",
        "oturum ac",
        "oturum aç",
        "user account control",
        "kullanici hesabi denetimi",
        "kullanıcı hesabı denetimi",
        "bitlocker",
        "authenticator"
    ];

    public static bool ShouldSkipScreenshot(
        string? windowTitle,
        string? processName,
        string? windowClassName)
    {
        if (ObservationUiCapture.ShouldSkipSelfWindow(processName, processName))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(processName) &&
            SensitiveProcessNames.Any(name =>
                processName.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(windowClassName) &&
            SensitiveWindowClasses.Any(cls =>
                windowClassName.Equals(cls, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return false;
        }

        var normalized = Normalize(windowTitle);
        return SensitiveTitleKeywords.Any(keyword =>
            normalized.Contains(Normalize(keyword), StringComparison.Ordinal));
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant()
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');
}
