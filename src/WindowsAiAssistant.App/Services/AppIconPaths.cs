namespace WindowsAiAssistant.App.Services;

public static class AppIconPaths
{
    public static string IcoPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    public static bool TryGetIcoPath(out string path)
    {
        path = IcoPath;
        return File.Exists(path);
    }
}
