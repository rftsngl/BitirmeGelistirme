using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WindowsAiAssistant.App.Background;
using WindowsAiAssistant.App.Integrations;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.Runtime.Observation;
namespace WindowsAiAssistant.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private BackgroundAssistantHost? _backgroundHost;

    public App()
    {
        UnhandledException += OnUnhandledException;

        _singleInstanceMutex = new Mutex(true, "WindowsAiAssistant_SingleInstance_v1", out var isNewInstance);
        if (!isNewInstance)
        {
            SingleInstanceCoordinator.TryNotifyPrimaryInstance("SHOW");
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Current?.Exit();
            return;
        }

        Services = AppServices.BuildServiceProvider();
        AppNotificationIdentity.EnsureRegistered();
        SingleInstanceCoordinator.StartListener(HandleSingleInstanceCommand);
        InitializeComponent();
    }

    private void HandleSingleInstanceCommand(string command)
    {
        if (Current is not App app || app._mainWindow is null)
        {
            return;
        }

        app._mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (command.Equals("SHOW", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("ACTIVATE", StringComparison.OrdinalIgnoreCase))
            {
                app._mainWindow.ShowFromTray();
                return;
            }

            if (command.Equals("OVERLAY", StringComparison.OrdinalIgnoreCase))
            {
                app._mainWindow.ShowFromTray();
                app._backgroundHost?.RequestOverlayActivation();
            }
        });
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        CrashLog.Write(e.Exception, "UnhandledException");
    }

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = Services.GetRequiredService<MainWindow>();
        var coordinator = Services.GetRequiredService<ActionApprovalCoordinator>();
        coordinator.SetDispatcherQueue(_mainWindow.DispatcherQueue);

        _backgroundHost = Services.GetRequiredService<BackgroundAssistantHost>();
        _backgroundHost.Start(_mainWindow);
        Services.GetRequiredService<ForegroundFocusService>().StartTracking();
        HandleJumpListActivation(args.Arguments);
    }

    private void HandleJumpListActivation(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return;
        }

        var navigation = Services.GetRequiredService<INavigationService>();
        _mainWindow?.ShowFromTray();

        if (arguments.Contains("--jump settings", StringComparison.OrdinalIgnoreCase))
        {
            navigation.NavigateToSettings();
            return;
        }

        if (arguments.Contains("--jump last", StringComparison.OrdinalIgnoreCase))
        {
            navigation.NavigateToHistory();
        }
    }

    internal static void ShutdownApplication()
    {
        if (Current is App app)
        {
            app._backgroundHost?.Dispose();
            if (Services is ServiceProvider provider)
            {
                provider.Dispose();
            }
        }

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        SingleInstanceCoordinator.StopListener();
        Current.Exit();
    }
}
