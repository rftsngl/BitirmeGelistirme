namespace WindowsAiAssistant.Runtime.Observation;

public sealed class ObservationService
{
    private readonly ForegroundWindowService _foregroundWindow;
    private readonly ScreenInfoService _screenInfo;

    public ObservationService(ForegroundWindowService foregroundWindow, ScreenInfoService screenInfo)
    {
        _foregroundWindow = foregroundWindow ?? throw new ArgumentNullException(nameof(foregroundWindow));
        _screenInfo = screenInfo ?? throw new ArgumentNullException(nameof(screenInfo));
    }

    public Task<DesktopObservation> CaptureAsync(
        ObservationCaptureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (windowTitle, processName, processId) = _foregroundWindow.GetForegroundInfo();
        var (screenWidth, screenHeight, cursorX, cursorY) = _screenInfo.GetScreenAndCursor();

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
            LastActionResult = options?.LastActionResult
        };

        return Task.FromResult(observation);
    }
}
