using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.HotKeys;
using WindowsAiAssistant.App.Overlay;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.Tray;

namespace WindowsAiAssistant.App.Background;

public sealed class BackgroundAssistantHost : IDisposable
{
    private readonly AudioOptions _audioOptions;
    private readonly TrayIconService _tray;
    private readonly GlobalHotKeyService _hotKeys;
    private readonly IWakeWordService _wakeWord;
    private readonly AssistantOverlayWindow _overlay;
    private MainWindow? _mainWindow;
    private int _overlayBusy;
    private bool _started;
    private bool _disposed;

    public BackgroundAssistantHost(
        AudioOptions audioOptions,
        TrayIconService tray,
        GlobalHotKeyService hotKeys,
        IWakeWordService wakeWord,
        AssistantOverlayWindow overlay)
    {
        _audioOptions = audioOptions ?? throw new ArgumentNullException(nameof(audioOptions));
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));
        _hotKeys = hotKeys ?? throw new ArgumentNullException(nameof(hotKeys));
        _wakeWord = wakeWord ?? throw new ArgumentNullException(nameof(wakeWord));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }

    public void Start(MainWindow mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        if (_started)
        {
            return;
        }

        _started = true;
        _mainWindow = mainWindow;

        if (!_audioOptions.BackgroundModeEnabled)
        {
            mainWindow.Activate();
            return;
        }

        _tray.Initialize();
        _tray.OpenMainWindowRequested += OnOpenMainWindow;
        _tray.ActivateOverlayRequested += OnActivateOverlay;
        _tray.ExitRequested += OnExit;
        _tray.ListeningToggled += OnListeningToggled;

        _hotKeys.HotKeyPressed += OnActivateOverlay;
        _wakeWord.WakeWordDetected += OnActivateOverlay;
        _hotKeys.Start();
        _ = _wakeWord.StartAsync();
        _tray.SetListeningEnabled(_audioOptions.GlobalHotKeyEnabled || _audioOptions.WakeWordEnabled);

        if (_audioOptions.StartWithWindows)
        {
            WindowsStartupService.SetEnabled(true);
        }

        if (_audioOptions.StartMinimizedToTray)
        {
            mainWindow.HideToTray();
        }
        else
        {
            mainWindow.Activate();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tray.OpenMainWindowRequested -= OnOpenMainWindow;
        _tray.ActivateOverlayRequested -= OnActivateOverlay;
        _tray.ExitRequested -= OnExit;
        _tray.ListeningToggled -= OnListeningToggled;
        _hotKeys.HotKeyPressed -= OnActivateOverlay;
        _wakeWord.WakeWordDetected -= OnActivateOverlay;

        _hotKeys.Dispose();
        _tray.Dispose();
        _ = _wakeWord.DisposeAsync();
    }

    private void OnOpenMainWindow(object? sender, EventArgs e)
    {
        _mainWindow?.ShowFromTray();
    }

    private async void OnActivateOverlay(object? sender, EventArgs e)
    {
        if (!_tray.IsListeningEnabled)
        {
            return;
        }

        // If a session is already running (e.g. TTS playback), cancel it.
        // The user can press the hotkey again to start a fresh session.
        if (Interlocked.CompareExchange(ref _overlayBusy, 1, 0) != 0)
        {
            _overlay.HideOverlay();
            return;
        }

        try
        {
            await _overlay.RunVoiceSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            Interlocked.Exchange(ref _overlayBusy, 0);
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _mainWindow?.RequestExit();
        App.ShutdownApplication();
    }

    private async void OnListeningToggled(object? sender, EventArgs e)
    {
        if (_tray.IsListeningEnabled)
        {
            await _wakeWord.StartAsync().ConfigureAwait(false);
        }
        else
        {
            await _wakeWord.StopAsync().ConfigureAwait(false);
        }
    }
}
