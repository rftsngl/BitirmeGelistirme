namespace WindowsAiAssistant.App.Services;

public static class LaunchArguments
{
    public const string BackgroundFlag = "--background";

    public static bool IsBackgroundLaunch(string[]? args = null)
    {
        args ??= Environment.GetCommandLineArgs();
        return args.Any(arg => arg.Equals(BackgroundFlag, StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildStartupCommand(bool startWithWindows, bool background = true)
    {
        _ = startWithWindows;
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return string.Empty;
        }

        return background
            ? $"\"{exePath}\" {BackgroundFlag}"
            : $"\"{exePath}\"";
    }
}
