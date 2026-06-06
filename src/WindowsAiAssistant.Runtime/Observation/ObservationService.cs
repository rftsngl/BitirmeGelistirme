using WindowsAiAssistant.Runtime.Automation;
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
        var windows = _windowManager.ListVisibleWindows();
        var monitors = _screenInfo.GetMonitorInfos();

        ScreenshotObservation? screenshot = null;
        if (!string.IsNullOrWhiteSpace(options?.RunId) && options.StepIndex is not null)
        {
            screenshot = _screenCapture.Capture(options.RunId, options.StepIndex.Value);
        }

        UiElementTree? uiTree = null;
        var foreground = windows.FirstOrDefault(window => window.IsForeground);
        if (foreground is not null)
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

        return new DesktopObservation
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
            Screenshot = screenshot,
            Windows = windows,
            UiTree = uiTree,
            Monitors = monitors
        };
    }
}
