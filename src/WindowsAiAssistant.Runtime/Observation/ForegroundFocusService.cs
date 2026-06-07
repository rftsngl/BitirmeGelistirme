using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Observation;

/// <summary>
/// Overlay acilmadan once odaktaki dis uygulamayi kaydeder; agent calismadan once geri yukler.
/// </summary>
public sealed class ForegroundFocusService
{
    private readonly WindowManager _windowManager;
    private ForegroundFocusSnapshot? _captured;

    public ForegroundFocusService(WindowManager windowManager) =>
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

    public ForegroundFocusSnapshot? Captured => _captured;

    public ForegroundFocusSnapshot? CaptureExternalForeground()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            _captured = null;
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        var processName = ResolveProcessName(processId);
        if (AgentSelfWindow.IsAssistantProcess(processName))
        {
            _captured = null;
            return null;
        }

        var title = ReadWindowTitle(handle);
        _captured = new ForegroundFocusSnapshot(handle, title, processName);
        return _captured;
    }

    public bool TryRestoreCaptured()
    {
        if (_captured is null || _captured.Handle == IntPtr.Zero)
        {
            return false;
        }

        return _windowManager.FocusWindow(_captured.Handle);
    }

    public void Clear() => _captured = null;

    private static string ReadWindowTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowText(handle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    private static string ResolveProcessName(uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
