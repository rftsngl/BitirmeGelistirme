using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Observation;

/// <summary>
/// Overlay acilmadan once odaktaki dis uygulamayi kaydeder; agent calismadan once geri yukler.
/// Sohbet modunda son odaklanan dis pencereyi WinEvent ile izler.
/// </summary>
public sealed class ForegroundFocusService : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0;

    private readonly WindowManager _windowManager;
    private ForegroundFocusSnapshot? _captured;
    private ForegroundFocusSnapshot? _lastExternal;
    private IntPtr _hookHandle;
    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private bool _disposed;

    public ForegroundFocusService(WindowManager windowManager) =>
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

    public ForegroundFocusSnapshot? Captured => _captured;

    public ForegroundFocusSnapshot? LastExternal => _lastExternal;

    public void StartTracking()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        _winEventDelegate = OnForegroundChanged;
        _hookHandle = NativeMethods.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            WineventOutOfContext);

        RememberExternalForeground(NativeMethods.GetForegroundWindow());
    }

    public ForegroundFocusSnapshot? CaptureExternalForeground()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            _captured = null;
            return null;
        }

        var snapshot = CreateExternalSnapshot(handle);
        _captured = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Overlay oturumunda acilis aninda kaydedilen pencereyi geri yukler.
    /// </summary>
    public bool TryRestoreCaptured()
    {
        if (_captured is null || _captured.Handle == IntPtr.Zero)
        {
            return false;
        }

        return _windowManager.FocusWindow(_captured.Handle);
    }

    /// <summary>
    /// Asistan odaktayken once oturum yakalamasi, yoksa son dis pencereye odagi geri verir.
    /// Zaten dis bir uygulama odaktaysa true doner.
    /// </summary>
    public bool TryRestoreForDesktopAutomation()
    {
        if (!IsAssistantForeground())
        {
            return true;
        }

        if (_captured is not null && _windowManager.FocusWindow(_captured.Handle))
        {
            return true;
        }

        if (_lastExternal is not null && _windowManager.FocusWindow(_lastExternal.Handle))
        {
            return true;
        }

        return false;
    }

    public void Clear() => _captured = null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _winEventDelegate = null;
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType != EventSystemForeground || hwnd == IntPtr.Zero)
        {
            return;
        }

        RememberExternalForeground(hwnd);
    }

    private void RememberExternalForeground(IntPtr handle)
    {
        var snapshot = CreateExternalSnapshot(handle);
        if (snapshot is not null)
        {
            _lastExternal = snapshot;
        }
    }

    private static ForegroundFocusSnapshot? CreateExternalSnapshot(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        var processName = ResolveProcessName(processId);
        if (AgentSelfWindow.IsAssistantProcess(processName))
        {
            return null;
        }

        var title = ReadWindowTitle(handle);
        return new ForegroundFocusSnapshot(handle, title, processName);
    }

    private static bool IsAssistantForeground()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        return AgentSelfWindow.IsAssistantProcess(ResolveProcessName(processId));
    }

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
