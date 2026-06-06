using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WindowsAiAssistant.App.Background;
using WindowsAiAssistant.App.Services;

namespace WindowsAiAssistant.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private BackgroundAssistantHost? _backgroundHost;

    public App()
    {
        _singleInstanceMutex = new Mutex(true, "WindowsAiAssistant_SingleInstance_v1", out var isNewInstance);
        if (!isNewInstance)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Current?.Exit();
            return;
        }

        Services = AppServices.BuildServiceProvider();
        InitializeComponent();
    }

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = Services.GetRequiredService<MainWindow>();
        var coordinator = Services.GetRequiredService<ActionApprovalCoordinator>();
        coordinator.SetDispatcherQueue(_mainWindow.DispatcherQueue);

        _backgroundHost = Services.GetRequiredService<BackgroundAssistantHost>();
        _backgroundHost.Start(_mainWindow);
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
        Current.Exit();
    }
}
