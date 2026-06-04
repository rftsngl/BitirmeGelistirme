using Microsoft.Extensions.DependencyInjection;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.App.Configuration;
using WindowsAiAssistant.App.ProviderSettings;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.ViewModels;
using WindowsAiAssistant.Runtime.Actions;
using WindowsAiAssistant.Runtime.Actions.Handlers;
using WindowsAiAssistant.Runtime.Logging;
using WindowsAiAssistant.Runtime.Observation;

namespace WindowsAiAssistant.App;

public static class AppServices
{
    public static ServiceProvider BuildServiceProvider()
    {
        var (agentOptions, runtimeOptions) = AppConfiguration.Load();
        var services = new ServiceCollection();

        services.AddSingleton(agentOptions);
        services.AddSingleton(runtimeOptions);
        services.AddSingleton(agentOptions.Model);
        services.AddSingleton<IProviderConfigurationService, ProviderConfigurationService>();
        services.AddSingleton<ForegroundWindowService>();
        services.AddSingleton<ScreenInfoService>();
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
        services.AddSingleton<ActionExecutor>();
        services.AddSingleton<RunLogger>();
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
