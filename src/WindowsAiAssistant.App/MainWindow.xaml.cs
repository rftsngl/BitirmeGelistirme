using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WindowsAiAssistant.App.Services;
using WindowsAiAssistant.App.Views;

namespace WindowsAiAssistant.App;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigationService;

    public MainWindow(INavigationService navigationService)
    {
        _navigationService = (NavigationService)navigationService
            ?? throw new ArgumentNullException(nameof(navigationService));

        InitializeComponent();

        Title = "Windows AI Assistant";
        TryEnableMicaBackdrop();
        ConfigureCustomTitleBar();

        _navigationService.Initialize(ContentFrame, ShellNav);
        Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        if (ShellNav.MenuItems.Count > 0 && ShellNav.MenuItems[0] is NavigationViewItem firstItem)
        {
            ShellNav.SelectedItem = firstItem;
        }
        else
        {
            _navigationService.NavigateToAssistant();
        }
    }

    private void TryEnableMicaBackdrop()
    {
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
    }

    private void ConfigureCustomTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void ShellNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        var pageType = tag switch
        {
            "Assistant" => typeof(AssistantPage),
            "History" => typeof(HistoryPage),
            "Capabilities" => typeof(CapabilitiesPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(AssistantPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, null,
                args.RecommendedNavigationTransitionInfo
                ?? new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
        }
    }
}
