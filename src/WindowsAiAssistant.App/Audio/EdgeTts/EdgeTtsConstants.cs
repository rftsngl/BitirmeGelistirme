namespace WindowsAiAssistant.App.Audio.EdgeTts;

internal static class EdgeTtsConstants
{
    public const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    public const string BaseUrl = "speech.platform.bing.com/consumer/speech/synthesize/readaloud";
    public const string ChromiumFullVersion = "143.0.3650.75";
    public const string SecMsGecVersion = $"1-{ChromiumFullVersion}";

    public static string WssUrl =>
        $"wss://{BaseUrl}/edge/v1?TrustedClientToken={TrustedClientToken}";

    public static string UserAgent =>
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        + $"(KHTML, like Gecko) Chrome/{ChromiumMajor}.0.0.0 Safari/537.36 "
        + $"Edg/{ChromiumMajor}.0.0.0";

    private static string ChromiumMajor => ChromiumFullVersion.Split('.')[0];
}
