using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace WindowsAiAssistant.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public App()
    {
        Services = AppServices.BuildServiceProvider();
        InitializeComponent();
    }

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();
    }
}
