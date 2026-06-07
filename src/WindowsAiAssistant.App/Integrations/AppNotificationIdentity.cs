using System.Runtime.InteropServices;

namespace WindowsAiAssistant.App.Integrations;

public static class AppNotificationIdentity
{
    public const string AppUserModelId = "WindowsAiAssistant.App";

    public static void EnsureRegistered()
    {
        _ = NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    }
}
