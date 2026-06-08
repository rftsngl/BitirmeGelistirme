using System.Diagnostics;

using WindowsAiAssistant.Runtime.Automation;

using WindowsAiAssistant.Runtime.Config;

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

    private readonly UiAutomationOptions _uiAutomationOptions;

    private readonly ObservationOptions _observationOptions;



    public ObservationService(

        ForegroundWindowService foregroundWindow,

        ScreenInfoService screenInfo,

        ScreenCaptureService screenCapture,

        WindowManager windowManager,

        UiAutomationService uiAutomation,

        RuntimeOptions runtimeOptions)

    {

        _foregroundWindow = foregroundWindow ?? throw new ArgumentNullException(nameof(foregroundWindow));

        _screenInfo = screenInfo ?? throw new ArgumentNullException(nameof(screenInfo));

        _screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));

        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));

        _uiAutomationOptions = runtimeOptions?.UiAutomation ?? new UiAutomationOptions();

        _observationOptions = runtimeOptions?.Observation ?? new ObservationOptions();

    }



    public async Task<DesktopObservation> CaptureAsync(

        ObservationCaptureOptions? options = null,

        CancellationToken cancellationToken = default)

    {

        cancellationToken.ThrowIfCancellationRequested();

        var started = Stopwatch.GetTimestamp();



        var foregroundDetails = _foregroundWindow.GetForegroundDetails();
        var windowTitle = foregroundDetails.WindowTitle;
        var processName = foregroundDetails.ProcessName;
        var processId = foregroundDetails.ProcessId;
        var skipScreenshot = SensitiveWindowPolicy.ShouldSkipScreenshot(
            windowTitle,
            processName,
            foregroundDetails.WindowClassName);

        var (screenWidth, screenHeight, cursorX, cursorY) = _screenInfo.GetScreenAndCursor();

        var windows = _windowManager.ListVisibleWindows();

        var fingerprint = ObservationFingerprint.Compute(

            windowTitle,

            processName,

            processId,

            cursorX,

            cursorY,

            windows.Count);



        var previousWindowTitle = options?.PreviousActiveWindowTitle;

        var previousProcessName = options?.PreviousActiveProcessName;

        var windowChanged = previousWindowTitle is not null &&

            (!string.Equals(previousWindowTitle, windowTitle, StringComparison.Ordinal) ||

             !string.Equals(previousProcessName, processName, StringComparison.Ordinal));



        var stepIndex = options?.StepIndex ?? 0;

        if (ObservationIncrementalPolicy.ShouldReuse(

                _observationOptions.EnableIncrementalReuse,

                _observationOptions.FullCaptureEveryNSteps,

                stepIndex,

                options?.LastActionResult,

                fingerprint,

                options?.PreviousObservation?.ObservationFingerprint))

        {

            var previous = options!.PreviousObservation!;

            var reused = new DesktopObservation

            {

                Timestamp = DateTimeOffset.UtcNow,

                ActiveWindowTitle = windowTitle,

                ActiveProcessName = processName,

                ActiveProcessId = processId,

                ScreenWidth = screenWidth,

                ScreenHeight = screenHeight,

                CursorX = cursorX,

                CursorY = cursorY,

                LastUserGoal = options.LastUserGoal,

                LastActionResult = options.LastActionResult,

                PreviousActiveWindowTitle = previousWindowTitle,

                ActiveWindowChanged = windowChanged,

                Screenshot = skipScreenshot ? null : previous.Screenshot,

                ScreenshotSkipReason = skipScreenshot
                    ? SensitiveWindowPolicy.ScreenshotSkipReason
                    : previous.ScreenshotSkipReason,

                Windows = windows,

                UiTree = previous.UiTree,

                UiCaptureSkipReason = previous.UiCaptureSkipReason,

                Monitors = previous.Monitors,

                ReusedFromPreviousStep = true,

                ObservationFingerprint = fingerprint,

                CaptureDurationMs = ElapsedMs(started)

            };



            DebugAgentLog.Write(

                "F017",

                "ObservationService.CaptureAsync",

                "observation reused",

                new { options.RunId, options.StepIndex, fingerprint, reused.CaptureDurationMs },

                options.RunId);



            return reused;

        }



        var monitors = _screenInfo.GetMonitorInfos();



        ScreenshotObservation? screenshot = null;
        string? screenshotSkipReason = skipScreenshot
            ? SensitiveWindowPolicy.ScreenshotSkipReason
            : null;

        if (!skipScreenshot &&
            !string.IsNullOrWhiteSpace(options?.RunId) &&
            options.StepIndex is not null)
        {
            screenshot = _screenCapture.Capture(options.RunId, options.StepIndex.Value);
        }



        UiElementTree? uiTree = null;

        string? uiCaptureSkipReason = null;

        var foreground = windows.FirstOrDefault(window => window.IsForeground);

        var skipSelfUi = ObservationUiCapture.ShouldSkipSelfWindow(

            processName,

            foreground?.ProcessName);

        if (skipSelfUi)

        {

            uiCaptureSkipReason = ObservationUiCapture.SelfWindowSkipReason;

        }

        else if (foreground is not null)

        {

            try

            {

                var timeoutMs = Math.Clamp(_uiAutomationOptions.CaptureTimeoutMs, 1000, 120_000);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                timeoutCts.CancelAfter(timeoutMs);

                uiTree = await _uiAutomation

                    .CaptureWindowTreeAsync(foreground.Handle, timeoutCts.Token)

                    .ConfigureAwait(false);

            }

            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)

            {

                uiCaptureSkipReason = ObservationUiCapture.FormatTimeout(_uiAutomationOptions.CaptureTimeoutMs);

            }

            catch (Exception ex)

            {

                uiCaptureSkipReason = ObservationUiCapture.FormatCaptureFailure(ex);

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

            ScreenshotSkipReason = screenshotSkipReason,

            Windows = windows,

            UiTree = uiTree,

            UiCaptureSkipReason = uiCaptureSkipReason,

            Monitors = monitors,

            ObservationFingerprint = fingerprint,

            CaptureDurationMs = ElapsedMs(started)

        };



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

                suspiciousLabels,

                observation.CaptureDurationMs

            },

            options?.RunId);



        return observation;

    }



    private static long ElapsedMs(long started) =>

        (long)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);

}


