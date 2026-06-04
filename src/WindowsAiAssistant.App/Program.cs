using Microsoft.Extensions.DependencyInjection;
using WindowsAiAssistant.App.ProviderSettings;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.ViewModels;

namespace WindowsAiAssistant.App;

public static class AppServices
{
    public static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ActiveProviderStatus>();
        services.AddSingleton<IActiveProviderStatus>(sp => sp.GetRequiredService<ActiveProviderStatus>());
        services.AddSingleton<ProviderSettingsViewModel>();
        services.AddSingleton<AssistantViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<CapabilitiesViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
