using Microsoft.Extensions.DependencyInjection;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Audio;
using WindowsAiAssistant.App.Background;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.HotKeys;
using WindowsAiAssistant.App.Overlay;
using WindowsAiAssistant.App.ProviderSettings;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.Tray;
using WindowsAiAssistant.App.ViewModels;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Actions.Handlers;
using WindowsAiAssistant.Runtime.Automation;
using WindowsAiAssistant.Runtime.Logging;
using WindowsAiAssistant.Runtime.Observation;
using WindowsAiAssistant.Runtime.Policy;
using WindowsAiAssistant.Runtime.Windows;

namespace WindowsAiAssistant.App;

public static class AppServices
{
    public static ServiceProvider BuildServiceProvider()
    {
        var (agentOptions, runtimeOptions, audioOptions) = AppConfiguration.Load();
        var services = new ServiceCollection();

        services.AddSingleton(agentOptions);
        services.AddSingleton(runtimeOptions);
        services.AddSingleton(runtimeOptions.ActionPolicy);
        services.AddSingleton<ActionGate>();
        services.AddSingleton<ActionApprovalCoordinator>();
        services.AddSingleton<IActionApprovalHandler>(sp => sp.GetRequiredService<ActionApprovalCoordinator>());
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
        services.AddSingleton<IActionHandler, RespondActionHandler>();
        services.AddSingleton<IActionHandler, AskUserActionHandler>();
        services.AddSingleton<IActionHandler, StopActionHandler>();
        services.AddSingleton<IActionHandler, WaitActionHandler>();
        services.AddSingleton<IActionHandler, OpenAppActionHandler>();
        services.AddSingleton<IActionHandler, OpenUrlActionHandler>();
        services.AddSingleton<IActionHandler, TypeTextActionHandler>();
        services.AddSingleton<IActionHandler, PressKeyActionHandler>();
        services.AddSingleton<IActionHandler, PressShortcutActionHandler>();
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
        services.AddSingleton<IActionHandler, MouseScrollActionHandler>();
        services.AddSingleton<IActionHandler, MouseDragActionHandler>();
        services.AddSingleton<ActionExecutor>();
        services.AddSingleton<RunLogger>();
        services.AddSingleton<RunLogReader>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<DecisionParser>();
        services.AddSingleton<AiClient>();
        services.AddSingleton<ProviderConnectionTester>();
        services.AddSingleton<AgentLoop>();

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ActiveProviderStatus>();
        services.AddSingleton<IActiveProviderStatus>(sp => sp.GetRequiredService<ActiveProviderStatus>());
        services.AddSingleton<ProviderSettingsViewModel>();
        services.AddSingleton<AssistantViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<CapabilitiesViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<ISpeechToTextService, WindowsSpeechToTextService>();
        services.AddSingleton<ITextToSpeechService, WindowsTextToSpeechService>();
        services.AddSingleton<IWakeWordService>(sp =>
        {
            var options = sp.GetRequiredService<AudioOptions>();
            if (options.WakeWordEnabled && !string.IsNullOrWhiteSpace(options.PorcupineAccessKey))
            {
                return new PorcupineWakeWordService(options);
            }

            return new NullWakeWordService();
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
