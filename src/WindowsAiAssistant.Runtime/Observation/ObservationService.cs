using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ObservationService
{
    private readonly ForegroundWindowService _foregroundWindow;
    private readonly ScreenInfoService _screenInfo;
    private readonly ScreenCaptureService _screenCapture;

    public ObservationService(
        ForegroundWindowService foregroundWindow,
        ScreenInfoService screenInfo,
        ScreenCaptureService screenCapture)
    {
        _foregroundWindow = foregroundWindow ?? throw new ArgumentNullException(nameof(foregroundWindow));
        _screenInfo = screenInfo ?? throw new ArgumentNullException(nameof(screenInfo));
        _screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
    }

    public Task<DesktopObservation> CaptureAsync(
        ObservationCaptureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (windowTitle, processName, processId) = _foregroundWindow.GetForegroundInfo();
        var (screenWidth, screenHeight, cursorX, cursorY) = _screenInfo.GetScreenAndCursor();

        ScreenshotObservation? screenshot = null;
        if (!string.IsNullOrWhiteSpace(options?.RunId) && options.StepIndex is not null)
        {
            screenshot = _screenCapture.Capture(options.RunId, options.StepIndex.Value);
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
            Screenshot = screenshot
        };

        return Task.FromResult(observation);
    }
}
