using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.ViewModels;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Core;
using WindowsAiAssistant.Infrastructure;
using WindowsAiAssistant.Infrastructure.Capabilities;
using WindowsAiAssistant.Infrastructure.Context;
using WindowsAiAssistant.Infrastructure.Grounding;
using WindowsAiAssistant.Infrastructure.Legacy;
using WindowsAiAssistant.Infrastructure.Launch;
using WindowsAiAssistant.Infrastructure.ActionPrimitives;
using WindowsAiAssistant.Infrastructure.Observation;
using WindowsAiAssistant.Infrastructure.Resolution;
using WindowsAiAssistant.Infrastructure.Verification;
using WindowsAiAssistant.Infrastructure.Secrets;
using WindowsAiAssistant.Infrastructure.State;

namespace WindowsAiAssistant.App;

public static class AppServices
{
    public static ServiceProvider BuildServiceProvider()
    {
        var configuration = BuildConfiguration();

        var services = new ServiceCollection();
        ConfigureLogging(services, configuration);
        ConfigureDependencies(services, configuration);

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
        logger.LogInformation("Application bootstrap started.");
        return provider;
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    private static void ConfigureLogging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.AddDebug();

            var minLevelText = configuration["Logging:LogLevel:Default"];
            if (Enum.TryParse<LogLevel>(minLevelText, true, out var minLevel))
            {
                builder.SetMinimumLevel(minLevel);
            }
            else
            {
                builder.SetMinimumLevel(LogLevel.Information);
            }
        });
    }

    private static void ConfigureDependencies(IServiceCollection services, IConfiguration configuration)
    {
        var executionPolicySettings = configuration
            .GetSection("ExecutionPolicy")
            .Get<ExecutionPolicySettings>() ?? new ExecutionPolicySettings();

        var modelDecisionSettings = configuration
            .GetSection("ModelDecision")
            .Get<ModelDecisionSettings>() ?? new ModelDecisionSettings();

        var builtInIds = (modelDecisionSettings.Profiles ?? [])
            .Select(p => p.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        var builtInSet = new BuiltInProfileSet(builtInIds);

        var userProfileStore = new JsonUserProviderProfileStore();
        var profileList = modelDecisionSettings.Profiles ?? [];
        foreach (var userProfile in userProfileStore.LoadAll())
        {
            if (!builtInSet.IsBuiltIn(userProfile.Id))
            {
                profileList.Add(userProfile);
            }
        }
        modelDecisionSettings = new ModelDecisionSettings
        {
            Enabled = modelDecisionSettings.Enabled,
            ActiveProfile = modelDecisionSettings.ActiveProfile,
            Profiles = profileList,
            TimeoutMilliseconds = modelDecisionSettings.TimeoutMilliseconds,
            RuntimeSliceFallbackPolicy = modelDecisionSettings.RuntimeSliceFallbackPolicy
        };

        services.AddSingleton(executionPolicySettings);
        services.AddSingleton(modelDecisionSettings);
        services.AddSingleton<IBuiltInProfileSet>(builtInSet);
        services.AddSingleton<IUserProviderProfileStore>(userProfileStore);
        services.AddSingleton<IAuditLogger, FileAuditLogger>();
        services.AddSingleton<ICommandHistoryService, FileCommandHistoryService>();
        services.AddSingleton<IObservationProvider, WindowsObservationProvider>();
        services.AddSingleton<ICommandObservationCollector, WindowsPassiveCommandObservationCollector>();
        services.AddSingleton<IContextAdapter, NotepadContextAdapter>();
        services.AddSingleton<IContextAdapter, GenericWindowContextAdapter>();
        services.AddSingleton<IContextAdapterResolver, ContextAdapterResolver>();
        services.AddSingleton<ITargetResolver, BasicTargetResolver>();
        services.AddSingleton<IVerificationPrimitive, ProcessPresenceVerificationPrimitive>();
        services.AddSingleton<IVerificationPrimitive, ForegroundAlignmentVerificationPrimitive>();
        services.AddSingleton<IVerificationPrimitive, TextInputPostconditionVerificationPrimitive>();
        services.AddSingleton<IVerificationPrimitive, KeyInteractionPostconditionVerificationPrimitive>();
        services.AddSingleton<IVerificationPrimitive, FileOpenPostconditionVerificationPrimitive>();
        services.AddSingleton<IVerificationPrimitive, NavigationDestinationVerificationPrimitive>();
        services.AddSingleton<IExecutionVerifier, ExecutionVerifier>();
        services.AddSingleton<ILaunchTargetResolver, LaunchTargetResolver>();
        services.AddSingleton<ITargetGrounder, ConfigurationTargetGrounder>();
        services.AddSingleton<ILaunchStrategy, ShellExecutablePathLaunchStrategy>();
        services.AddSingleton<ILaunchStrategy, ShellApplicationLaunchStrategy>();
        services.AddSingleton<ILaunchStrategyResolver, LaunchStrategyResolver>();
        services.AddSingleton<ILauncher, Launcher>();
        services.AddSingleton<IKeyboardInputGateway, User32KeyboardInputGateway>();
        services.AddSingleton<IActionPrimitiveHandler, LaunchTargetPrimitiveHandler>();
        services.AddSingleton<IActionPrimitiveHandler, WindowsNativeInputPrimitiveHandler>();
        services.AddSingleton<IActionPrimitiveHandler, WaitPrimitiveHandler>();
        services.AddSingleton<IActionPrimitiveExecutor, ActionPrimitiveExecutor>();
        services.AddSingleton<ICapability, ApplicationCapability>();
        services.AddSingleton<ICapability, WindowProcessCapability>();
        services.AddSingleton<ICapability, ProcessVerificationCapability>();
        services.AddSingleton<ICapability, ForegroundAlignmentCapability>();
        services.AddSingleton<ICapability, ServiceStatusCapability>();
        services.AddSingleton<ICapability, ServiceControlCapability>();
        services.AddSingleton<ICapability, FileVerificationCapability>();
        services.AddSingleton<ICapability, FileOpenCapability>();
        services.AddSingleton<ICapability, NoOpCapability>();
        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.AddSingleton<FileStepRuntimeEngagementBoundary>();
        services.AddSingleton<FileStepEntryDecider>();
        services.AddSingleton<FileStepFollowUpDecider>();
        services.AddSingleton<FileRuntimeSliceRunner>();
        services.AddSingleton<ServiceStepRuntimeEngagementBoundary>();
        services.AddSingleton<ServiceStepEntryDecider>();
        services.AddSingleton<ServiceStepFollowUpDecider>();
        services.AddSingleton<ServiceRuntimeSliceRunner>();
        services.AddSingleton<IStepRuntimeEngagementBoundary, ProcessWindowStepRuntimeEngagementBoundary>();
        services.AddSingleton<IStepEntryDecider, ProcessWindowStepEntryDecider>();
        services.AddSingleton<ProcessWindowStepFollowUpDecider>();
        services.AddSingleton<IStepFollowUpDecider>(sp => sp.GetRequiredService<ProcessWindowStepFollowUpDecider>());
        services.AddSingleton<ProcessWindowRuntimeSliceRunner>();
        services.AddSingleton<IRuntimeSlice>(sp => sp.GetRequiredService<ProcessWindowRuntimeSliceRunner>());
        services.AddSingleton<IRuntimeSlice>(sp => sp.GetRequiredService<FileRuntimeSliceRunner>());
        services.AddSingleton<IRuntimeSlice>(sp => sp.GetRequiredService<ServiceRuntimeSliceRunner>());
        services.AddSingleton<IRuntimeSliceRegistry, RuntimeSliceRegistry>();
        services.AddSingleton<ICapabilityInvocationRouter, WindowsAiAssistant.Core.Routing.DefaultCapabilityInvocationRouter>();
        services.AddSingleton<ICapabilityInvocationRouterRegistry, WindowsAiAssistant.Core.Routing.CapabilityInvocationRouterRegistry>();
        services.AddSingleton<ISafetyGate, BasicSafetyGate>();
        services.AddSingleton<IStepSafetyEvaluator, BasicStepSafetyEvaluator>();
        services.AddSingleton(sp =>
        {
            var modelSettings = sp.GetRequiredService<ModelDecisionSettings>();
            var client = new HttpClient();
            if (modelSettings.TimeoutMilliseconds > 0)
            {
                client.Timeout = TimeSpan.FromMilliseconds(modelSettings.TimeoutMilliseconds);
            }

            return client;
        });
        services.AddSingleton<IModelProviderSecretStore, DpapiModelProviderSecretStore>();
        services.AddSingleton<ModelProviderCredentialResolver>();
        services.AddSingleton<IActiveProfileStore, JsonActiveProfileStore>();
        services.AddSingleton<ModelDecisionProfileResolver>();
        services.AddSingleton<ModelDecisionProviderFactory>();
        services.AddSingleton<ProviderSettings.ProviderConnectionTester>();
        services.AddSingleton<RefreshableModelDecisionProvider>(sp =>
            new RefreshableModelDecisionProvider(() => sp.GetRequiredService<ModelDecisionProviderFactory>().Create()));
        services.AddSingleton<IModelDecisionProvider>(sp => sp.GetRequiredService<RefreshableModelDecisionProvider>());
        services.AddSingleton<ProviderSettings.ProviderSettingsViewModel>();
        services.AddSingleton<IAiDecisionClient, ModelBackedDecisionClient>();
        services.AddSingleton<ITool>(sp =>
            new OpenAppTool(sp.GetRequiredService<IActionPrimitiveExecutor>()));
        services.AddSingleton<ITool, TypeTextTool>();
        services.AddSingleton<ITool, PressKeyTool>();
        services.AddSingleton<ITool, PressShortcutTool>();
        services.AddSingleton<StepRuntimeCoordinator>();
        services.AddSingleton(sp =>
        {
            var modelSettings = sp.GetRequiredService<ModelDecisionSettings>();
            return ActivatorUtilities.CreateInstance<AssistantOrchestrator>(
                sp,
                modelSettings.RuntimeSliceFallbackPolicy);
        });
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IActiveProviderStatus, ActiveProviderStatus>();
        services.AddSingleton<AssistantViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<CapabilitiesViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
