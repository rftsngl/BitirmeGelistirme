using WindowsAiAssistant.Runtime.Automation;
using WindowsAiAssistant.Runtime.Debugging;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ObservationService
{
    private readonly ForegroundWindowService _foregroundWindow;
    private readonly ScreenInfoService _screenInfo;
    private readonly ScreenCaptureService _screenCapture;
    private readonly WindowManager _windowManager;
    private readonly UiAutomationService _uiAutomation;

    public ObservationService(
        ForegroundWindowService foregroundWindow,
        ScreenInfoService screenInfo,
        ScreenCaptureService screenCapture,
        WindowManager windowManager,
        UiAutomationService uiAutomation)
    {
        _foregroundWindow = foregroundWindow ?? throw new ArgumentNullException(nameof(foregroundWindow));
        _screenInfo = screenInfo ?? throw new ArgumentNullException(nameof(screenInfo));
        _screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));
    }

    public async Task<DesktopObservation> CaptureAsync(
        ObservationCaptureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (windowTitle, processName, processId) = _foregroundWindow.GetForegroundInfo();
        var (screenWidth, screenHeight, cursorX, cursorY) = _screenInfo.GetScreenAndCursor();

        var previousWindowTitle = options?.PreviousActiveWindowTitle;
        var previousProcessName = options?.PreviousActiveProcessName;
        var windowChanged = previousWindowTitle is not null &&
            (!string.Equals(previousWindowTitle, windowTitle, StringComparison.Ordinal) ||
             !string.Equals(previousProcessName, processName, StringComparison.Ordinal));
        var windows = _windowManager.ListVisibleWindows();
        var monitors = _screenInfo.GetMonitorInfos();

        ScreenshotObservation? screenshot = null;
        if (!string.IsNullOrWhiteSpace(options?.RunId) && options.StepIndex is not null)
        {
            screenshot = _screenCapture.Capture(options.RunId, options.StepIndex.Value);
        }

        UiElementTree? uiTree = null;
        string? uiCaptureSkipReason = null;
        var foreground = windows.FirstOrDefault(window => window.IsForeground);
        var skipSelfUi = AgentSelfWindow.IsAssistantProcess(processName) ||
                         (foreground is not null && AgentSelfWindow.IsAssistantProcess(foreground.ProcessName));
        if (skipSelfUi)
        {
            uiCaptureSkipReason =
                "atlandi (Windows AI Assistant penceresi — bu UI otomatiklestirilmez; open_app/shell/focus_window kullanin)";
        }
        else if (foreground is not null)
        {
            try
            {
                uiTree = await _uiAutomation
                    .CaptureWindowTreeAsync(foreground.Handle, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // UIA capture is best-effort; observation still returns metadata + screenshot.
            }
        }

        var observation = new DesktopObservation
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActiveWindowTitle = windowTitle,
            ActiveProcessName = processName,
            ActiveProcessId = processId,
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
            CursorX = cursorX,
            CursorY = cursorY,
            LastUserGoal = options?.LastUserGoal,
            LastActionResult = options?.LastActionResult,
            PreviousActiveWindowTitle = previousWindowTitle,
            ActiveWindowChanged = windowChanged,
            Screenshot = screenshot,
            Windows = windows,
            UiTree = uiTree,
            UiCaptureSkipReason = uiCaptureSkipReason,
            Monitors = monitors
        };

        // #region agent log
        var isSelfWindow = skipSelfUi;
        var uiElements = uiTree?.Elements ?? Array.Empty<UiElementSnapshot>();
        var suspiciousLabels = uiElements
            .Where(e =>
                (e.Name?.Contains("ONAY", StringComparison.OrdinalIgnoreCase) == true) ||
                (e.Name?.Contains("REDD", StringComparison.OrdinalIgnoreCase) == true) ||
                (e.ElementId.Contains("onay", StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .Select(e => new { e.ElementId, e.Name, e.ControlType })
            .ToList();
        DebugAgentLog.Write(
            "H1",
            "ObservationService.CaptureAsync",
            "observation captured",
            new
            {
                options?.RunId,
                options?.StepIndex,
                processName,
                windowTitle,
                isSelfWindow,
                uiElementCount = uiElements.Count,
                uiCaptureSkipReason,
                lastActionResult = options?.LastActionResult,
                suspiciousLabels
            },
            options?.RunId);
        // #endregion

        return observation;
    }
}
