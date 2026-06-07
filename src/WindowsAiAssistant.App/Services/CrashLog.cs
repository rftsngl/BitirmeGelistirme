namespace WindowsAiAssistant.App.Services;

internal static class CrashLog
{
    public static void Write(Exception exception, string? context = null)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var header = context is null
                ? $"[{DateTimeOffset.Now:u}]"
                : $"[{DateTimeOffset.Now:u}] {context}";
            File.AppendAllText(logPath, $"{header}\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
