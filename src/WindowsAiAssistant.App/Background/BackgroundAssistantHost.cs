using Microsoft.Extensions.DependencyInjection;
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
    private readonly SpeechWarmupService _speechWarmup;
    private readonly AssistantOverlayWindow _overlay;
    private MainWindow? _mainWindow;
    private int _overlayBusy;
    private bool _started;
    private bool _trayActive;
    private bool _disposed;

    public BackgroundAssistantHost(
        AudioOptions audioOptions,
        TrayIconService tray,
        GlobalHotKeyService hotKeys,
        IWakeWordService wakeWord,
        SpeechWarmupService speechWarmup,
        AssistantOverlayWindow overlay)
    {
        _audioOptions = audioOptions ?? throw new ArgumentNullException(nameof(audioOptions));
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));
        _hotKeys = hotKeys ?? throw new ArgumentNullException(nameof(hotKeys));
        _wakeWord = wakeWord ?? throw new ArgumentNullException(nameof(wakeWord));
        _speechWarmup = speechWarmup ?? throw new ArgumentNullException(nameof(speechWarmup));
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

        if (!_audioOptions.BackgroundModeEnabled && !LaunchArguments.IsBackgroundLaunch())
        {
            mainWindow.Activate();
            return;
        }

        _tray.Initialize();
        _trayActive = true;
        _tray.OpenMainWindowRequested += OnOpenMainWindow;
        _tray.ActivateOverlayRequested += OnActivateOverlay;
        _tray.ExitRequested += OnExit;
        _tray.ListeningToggled += OnListeningToggled;

        _hotKeys.HotKeyPressed += OnActivateOverlay;
        _wakeWord.WakeWordDetected += OnActivateOverlay;
        _hotKeys.Start();
        _ = StartWakeWordIfEnabledAsync();
        _ = RequestMicrophonePermissionInBackgroundAsync();
        SafeFireAndForget.Run(() => _speechWarmup.WarmupAsync(), nameof(SpeechWarmupService.WarmupAsync));
        _tray.SetListeningEnabled(_audioOptions.GlobalHotKeyEnabled || _audioOptions.WakeWordEnabled);

        if (_audioOptions.StartWithWindows)
        {
            WindowsStartupService.SetEnabled(true);
        }

        if (LaunchArguments.IsBackgroundLaunch())
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

    public void RequestOverlayActivation()
    {
        OnActivateOverlay(this, EventArgs.Empty);
    }

    public async Task<string?> ApplyRuntimeSettingsAsync()
    {
        WindowsStartupService.SetEnabled(_audioOptions.StartWithWindows);

        if (!_trayActive)
        {
            return "Tepsi/arka plan modu degisikligi bir sonraki uygulama acilisinda gecerli olur.";
        }

        var notes = new List<string>();
        var hotKeyError = _hotKeys.Restart();
        if (hotKeyError is not null)
        {
            notes.Add(hotKeyError);
        }

        var listening = _audioOptions.GlobalHotKeyEnabled || _audioOptions.WakeWordEnabled;
        _tray.SetListeningEnabled(listening);

        await _wakeWord.StopAsync().ConfigureAwait(false);
        if (_audioOptions.WakeWordEnabled && listening)
        {
            await StartWakeWordIfEnabledAsync().ConfigureAwait(false);
        }

        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    private Task StartWakeWordIfEnabledAsync()
    {
        if (!_audioOptions.WakeWordEnabled)
        {
            return Task.CompletedTask;
        }

        return _wakeWord.StartAsync();
    }

    private void OnOpenMainWindow(object? sender, EventArgs e)
    {
        _mainWindow?.ShowFromTray();
    }

    private void OnActivateOverlay(object? sender, EventArgs e) =>
        SafeFireAndForget.Run(() => OnActivateOverlayAsync(), nameof(OnActivateOverlay));

    private async Task OnActivateOverlayAsync()
    {
        if (!_tray.IsListeningEnabled)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _overlayBusy, 1, 0) != 0)
        {
            _overlay.HideOverlay();
            return;
        }

        var resumeWakeWord = _audioOptions.WakeWordEnabled && _tray.IsListeningEnabled;
        try
        {
            if (resumeWakeWord)
            {
                await _wakeWord.StopAsync().ConfigureAwait(false);
                await Task.Delay(320).ConfigureAwait(false);
            }

            await _overlay.RunVoiceSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            Interlocked.Exchange(ref _overlayBusy, 0);
            if (resumeWakeWord && _tray.IsListeningEnabled)
            {
                try
                {
                    await _wakeWord.StartAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex, $"{nameof(OnActivateOverlayAsync)}.ResumeWakeWord");
                }
            }
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _mainWindow?.RequestExit();
        App.ShutdownApplication();
    }

    private async Task RequestMicrophonePermissionInBackgroundAsync()
    {
        try
        {
            var permissions = App.Services.GetRequiredService<MicrophonePermissionService>();
            await permissions.RequestAccessAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, nameof(RequestMicrophonePermissionInBackgroundAsync));
        }
    }

    private void OnListeningToggled(object? sender, EventArgs e) =>
        SafeFireAndForget.Run(() => OnListeningToggledAsync(), nameof(OnListeningToggled));

    private async Task OnListeningToggledAsync()
    {
        if (_tray.IsListeningEnabled)
        {
            await StartWakeWordIfEnabledAsync().ConfigureAwait(false);
        }
        else
        {
            await _wakeWord.StopAsync().ConfigureAwait(false);
        }
    }
}
