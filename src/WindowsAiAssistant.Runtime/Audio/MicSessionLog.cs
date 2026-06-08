namespace WindowsAiAssistant.Runtime.Audio;

internal static class MicSessionLog
{
    public static void Write(string message)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "mic-session.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:u}] {message}\r\n");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
