using Microsoft.Extensions.DependencyInjection;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Agent.Dispatch;
using WindowsAiAssistant.App.Integrations;
using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Background;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.HotKeys;
using WindowsAiAssistant.App.Overlay;
using WindowsAiAssistant.App.ProviderSettings;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.Tray;
using WindowsAiAssistant.App.ViewModels;
using WindowsAiAssistant.Runtime.Audio;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Actions.Handlers;
using WindowsAiAssistant.Runtime.Automation;
using WindowsAiAssistant.Runtime.Debugging;
using WindowsAiAssistant.Runtime.Logging;
using WindowsAiAssistant.Runtime.Observation;
using WindowsAiAssistant.Runtime.Policy;
using WindowsAiAssistant.Runtime.Integrations;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.App;

public static class AppServices
{
    public static ServiceProvider BuildServiceProvider()
    {
        var (agentOptions, runtimeOptions, audioOptions) = AppConfiguration.Load();
        DebugAgentLog.Configure(runtimeOptions);
        var services = new ServiceCollection();

        services.AddSingleton(agentOptions);
        services.AddSingleton(runtimeOptions);
        services.AddSingleton(runtimeOptions.ActionPolicy);
        services.AddSingleton<ActionGate>();
        services.AddSingleton<ActionApprovalCoordinator>();
        services.AddSingleton<IActionApprovalHandler>(sp => sp.GetRequiredService<ActionApprovalCoordinator>());
        services.AddSingleton<AgentRunCoordinator>();
        services.AddSingleton<LocalAppSettingsService>();
        services.AddSingleton(audioOptions);
        services.AddSingleton(agentOptions.Model);
        services.AddSingleton<IProviderConfigurationService, ProviderConfigurationService>();
        services.AddSingleton<ForegroundWindowService>();
        services.AddSingleton<ScreenInfoService>();
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<WindowManager>();
        services.AddSingleton<UiElementRegistry>();
        services.AddSingleton<UiAutomationService>();
        services.AddSingleton<ObservationService>();
        services.AddSingleton<IWmiQueryService, WmiQueryService>();
        services.AddSingleton<ITaskSchedulerIntegrationService, TaskSchedulerIntegrationService>();
        services.AddSingleton<IComAutomationService, ComAutomationService>();
        services.AddSingleton<GlobalHookService>();
        services.AddSingleton<IGlobalHookService>(sp => sp.GetRequiredService<GlobalHookService>());
        services.AddSingleton<IGraphicsCaptureService, WinGraphicsCaptureService>();
        services.AddSingleton<IToastNotificationService, WinToastNotificationService>();
        services.AddSingleton<IWindowsHelloService, WinWindowsHelloService>();
        services.AddSingleton<IJumpListService, WinJumpListService>();
        services.AddSingleton<IServiceControlService, ServiceControlService>();
        services.AddSingleton<IEventLogService, EventLogIntegrationService>();
        services.AddSingleton<IRegistryOperationService, RegistryOperationService>();
        services.AddSingleton<IClipboardIntegrationService, ClipboardIntegrationService>();
        services.AddSingleton<IPackageInstallService, PackageInstallService>();
        services.AddSingleton<INetworkStatusService, NetworkStatusService>();
        services.AddSingleton<IAudioPowerService, AudioPowerService>();
        services.AddSingleton<IPerformanceCounterService, PerformanceCounterService>();
        services.AddSingleton<IFileSearchService, WinFileSearchService>();
        services.AddSingleton<INotificationListenerService, NotificationListenerService>();
        services.AddSingleton<ShellSessionService>();
        services.AddSingleton<IShellSessionService>(sp => sp.GetRequiredService<ShellSessionService>());
        services.AddSingleton<FileWatchIntegrationService>();
        services.AddSingleton<IFileWatchService>(sp => sp.GetRequiredService<FileWatchIntegrationService>());
        services.AddSingleton<ICredentialStoreService, CredentialStoreService>();
        services.AddSingleton<IActionHandler, RespondActionHandler>();
        services.AddSingleton<IActionHandler, AskUserActionHandler>();
        services.AddSingleton<IActionHandler, StopActionHandler>();
        services.AddSingleton<IActionHandler, WaitActionHandler>();
        services.AddSingleton<IActionHandler, OpenAppActionHandler>();
        services.AddSingleton<IActionHandler, OpenUrlActionHandler>();
        services.AddSingleton<IActionHandler, TypeTextActionHandler>();
        services.AddSingleton<IActionHandler, PressKeyActionHandler>();
        services.AddSingleton<IActionHandler, PressShortcutActionHandler>();
        services.AddSingleton<IActionHandler, SelectTextActionHandler>();
        services.AddSingleton<IActionHandler, ClickElementActionHandler>();
        services.AddSingleton<IActionHandler, FocusElementActionHandler>();
        services.AddSingleton<IActionHandler, ReadElementActionHandler>();
        services.AddSingleton<IActionHandler, SetValueActionHandler>();
        services.AddSingleton<IActionHandler, SelectElementActionHandler>();
        services.AddSingleton<IActionHandler, ExpandCollapseActionHandler>();
        services.AddSingleton<IActionHandler, InvokeToggleActionHandler>();
        services.AddSingleton<IActionHandler, ScrollActionHandler>();
        services.AddSingleton<IActionHandler, FocusWindowActionHandler>();
        services.AddSingleton<IActionHandler, WindowStateActionHandler>();
        services.AddSingleton<IActionHandler, MoveWindowActionHandler>();
        services.AddSingleton<IActionHandler, ListWindowsActionHandler>();
        services.AddSingleton<IActionHandler, LaunchActionHandler>();
        services.AddSingleton<IActionHandler, MouseClickActionHandler>();
        services.AddSingleton<IActionHandler, MouseMoveActionHandler>();
        services.AddSingleton<IActionHandler, MouseScrollActionHandler>();
        services.AddSingleton<IActionHandler, MouseDragActionHandler>();
        services.AddSingleton<IActionHandler, ShellActionHandler>();
        services.AddSingleton<IActionHandler, CaptureScreenActionHandler>();
        services.AddSingleton<IActionHandler, NotifyActionHandler>();
        services.AddSingleton<IActionHandler, WmiQueryActionHandler>();
        services.AddSingleton<IActionHandler, ScheduleTaskActionHandler>();
        services.AddSingleton<IActionHandler, JumpListActionHandler>();
        services.AddSingleton<IActionHandler, ComInvokeActionHandler>();
        services.AddSingleton<IActionHandler, VerifyUserActionHandler>();
        services.AddSingleton<IActionHandler, GlobalHookActionHandler>();
        services.AddSingleton<IActionHandler, ServiceControlActionHandler>();
        services.AddSingleton<IActionHandler, EventLogActionHandler>();
        services.AddSingleton<IActionHandler, RegistryOpActionHandler>();
        services.AddSingleton<IActionHandler, ClipboardActionHandler>();
        services.AddSingleton<IActionHandler, InstallPackageActionHandler>();
        services.AddSingleton<IActionHandler, NetworkStatusActionHandler>();
        services.AddSingleton<IActionHandler, AudioPowerActionHandler>();
        services.AddSingleton<IActionHandler, PerfCounterActionHandler>();
        services.AddSingleton<IActionHandler, FileSearchActionHandler>();
        services.AddSingleton<IActionHandler, NotificationListenActionHandler>();
        services.AddSingleton<IActionHandler, ShellSessionActionHandler>();
        services.AddSingleton<IActionHandler, FileWatchActionHandler>();
        services.AddSingleton<IActionHandler, CredentialStoreActionHandler>();
        services.AddSingleton<ActionExecutor>();
        services.AddSingleton<RunLogger>();
        services.AddSingleton<RunLogReader>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<DecisionParser>();
        services.AddSingleton<ITaskDispatchRouter, TaskDispatchRouter>();
        services.AddSingleton<AiClient>();
        services.AddSingleton<ProviderConnectionTester>();
        services.AddSingleton<AgentLoop>();

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ActiveProviderStatus>();
        services.AddSingleton<IActiveProviderStatus>(sp => sp.GetRequiredService<ActiveProviderStatus>());
        services.AddSingleton<ProviderSettingsViewModel>();
        services.AddSingleton<AppSettingsViewModel>();
        services.AddSingleton<AssistantViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<CapabilitiesViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<MicrophoneDeviceService>();
        services.AddSingleton<MicrophonePermissionService>();
        services.AddSingleton<MicrophoneSessionCoordinator>();
        services.AddSingleton<VoskWakeWordModelService>();
        services.AddSingleton<WhisperModelService>();
        services.AddSingleton<SpeechModelInventoryService>();
        services.AddSingleton<ForegroundFocusService>();
        services.AddSingleton<SpeechReadinessService>();
        services.AddSingleton<VoiceApprovalService>();
        services.AddSingleton<SwitchableSpeechToTextService>(sp =>
        {
            var options = sp.GetRequiredService<AudioOptions>();
            var readiness = sp.GetRequiredService<SpeechReadinessService>();
            var voskModels = sp.GetRequiredService<VoskWakeWordModelService>();
            var whisperModels = sp.GetRequiredService<WhisperModelService>();
            var microphone = sp.GetRequiredService<MicrophoneSessionCoordinator>();
            return new SwitchableSpeechToTextService(() =>
                SpeechEngineResolver.Create(options, readiness, voskModels, whisperModels, microphone));
        });
        services.AddSingleton<ISpeechToTextService>(sp => sp.GetRequiredService<SwitchableSpeechToTextService>());
        services.AddSingleton<SpeechWarmupService>();
        services.AddSingleton<EdgeTextToSpeechService>();
        services.AddSingleton<WindowsTextToSpeechService>();
        services.AddSingleton<ITextToSpeechService, HybridTextToSpeechService>();
        services.AddSingleton<IWakeWordService>(sp =>
        {
            var options = sp.GetRequiredService<AudioOptions>();
            var readiness = sp.GetRequiredService<SpeechReadinessService>();
            var models = sp.GetRequiredService<VoskWakeWordModelService>();
            var microphone = sp.GetRequiredService<MicrophoneSessionCoordinator>();
            return new VoskWakeWordService(options, readiness, models, microphone);
        });
        services.AddSingleton<GlobalHotKeyService>();
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<OverlaySessionRunner>();
        services.AddSingleton<AssistantOverlayWindow>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<BackgroundAssistantHost>();

        var provider = services.BuildServiceProvider();
        var configurationService = provider.GetRequiredService<IProviderConfigurationService>();
        configurationService.Initialize();
        SyncActiveProviderStatus(provider, configurationService);
        return provider;
    }

    private static void SyncActiveProviderStatus(
        ServiceProvider provider,
        IProviderConfigurationService configurationService)
    {
        var active = configurationService.ActiveProfile;
        if (active is null)
        {
            return;
        }

        var hasKey = configurationService.HasSavedApiKey(active.Id) ||
                     (!string.IsNullOrWhiteSpace(active.ApiKeyEnvVar) &&
                      !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(active.ApiKeyEnvVar)));

        provider.GetRequiredService<ActiveProviderStatus>().SetProfile(active, hasKey);
    }
}
