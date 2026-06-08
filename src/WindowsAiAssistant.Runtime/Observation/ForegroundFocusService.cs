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
    private string? _sessionUserGoal;
    private IntPtr _hookHandle;
    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private bool _disposed;

    public string? LastPrepareFailureReason { get; private set; }

    private static readonly (string[] Keywords, string[] ProcessNames, string[] TitleHints)[] GoalAppHints =
    [
        (["cursor"], ["Cursor"], ["Cursor"]),
        (["word", "winword", "belge", "belgesi", "belgeye"], ["WINWORD"], ["Word", "Belge"]),
        (["chrome"], ["chrome"], ["Chrome", "Google Chrome"]),
        (["edge", "msedge"], ["msedge"], ["Edge", "Microsoft Edge"]),
        (["firefox"], ["firefox"], ["Firefox"]),
        (["vscode", "visual studio code"], ["Code"], ["Visual Studio Code"]),
        (["notepad", "not defteri"], ["notepad"], ["Not Defteri", "Notepad"]),
        (["discord"], ["Discord"], ["Discord"]),
        (["spotify"], ["Spotify"], ["Spotify"]),
        (["excel"], ["EXCEL"], ["Excel"]),
        (["powerpoint"], ["POWERPNT"], ["PowerPoint"]),
    ];

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

    public void BeginAutomationSession(string? userGoal) => _sessionUserGoal = userGoal;

    public bool TryPrepareForDesktopAutomation(int maxAttempts = 3, int delayMs = 150)
    {
        LastPrepareFailureReason = null;

        if (!IsAssistantForeground())
        {
            return true;
        }

        var attempts = Math.Clamp(maxAttempts, 1, 5);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (TryPrepareOnce())
            {
                return true;
            }

            if (attempt < attempts)
            {
                Thread.Sleep(Math.Clamp(delayMs, 50, 1000));
            }
        }

        var (_, processName, _) = ReadForegroundInfo();
        LastPrepareFailureReason =
            $"Odak geri yuklenemedi ({attempts} deneme). Odakli process: '{processName}'. " +
            "focus_window ile hedef uygulamaya gecin.";
        return false;
    }

    private bool TryPrepareOnce()
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

        return TryFocusWindowFromUserGoal(_sessionUserGoal);
    }

    private static (string Title, string ProcessName, int ProcessId) ReadForegroundInfo()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return (string.Empty, string.Empty, 0);
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        return (ReadWindowTitle(handle), ResolveProcessName(processId), (int)processId);
    }

    public void Clear()
    {
        _captured = null;
        _sessionUserGoal = null;
        LastPrepareFailureReason = null;
    }

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

    private bool TryFocusWindowFromUserGoal(string? userGoal)
    {
        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return false;
        }

        var normalizedGoal = NormalizeGoalText(userGoal);
        var windows = _windowManager.ListVisibleWindows()
            .Where(window => !AgentSelfWindow.IsAssistantProcess(window.ProcessName))
            .ToList();

        foreach (var hint in GoalAppHints)
        {
            if (!hint.Keywords.Any(keyword => normalizedGoal.Contains(NormalizeGoalText(keyword), StringComparison.Ordinal)))
            {
                continue;
            }

            var match = windows.FirstOrDefault(window =>
                hint.ProcessNames.Any(process =>
                    window.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase)) ||
                hint.TitleHints.Any(title =>
                    window.Title.Contains(title, StringComparison.OrdinalIgnoreCase)));

            if (match is not null && _windowManager.FocusWindow(match.Handle))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeGoalText(string text) =>
        text.Trim().ToLowerInvariant()
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

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
