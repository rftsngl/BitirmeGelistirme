using System.Runtime.InteropServices;

namespace WindowsAiAssistant.Runtime.Automation;

internal static class AutomationNativeMethods
{
    private const uint WmGetObject = 0x003D;
    private const int ObjidClient = unchecked((int)0xFFFFFFFC);
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMs,
        out IntPtr result);

    /// <summary>
    /// Chromium/Electron tabanli uygulamalar erisilebilirlik agacini yalnizca bir
    /// WM_GETOBJECT/OBJID_CLIENT istegi geldiginde insa eder. Bu cagri agaci uyandirip
    /// UIA capture'in dolu donmesini saglar. Hatalar yutulur; capture yine de denenir.
    /// </summary>
    public static void TryWakeAccessibility(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return;
        }

        try
        {
            SendMessageTimeout(
                windowHandle,
                WmGetObject,
                IntPtr.Zero,
                (IntPtr)ObjidClient,
                SmtoAbortIfHung,
                250,
                out _);
        }
        catch
        {
            // Best-effort; capture devam eder.
        }
    }
}
