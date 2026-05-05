using Microsoft.UI.Xaml.Controls;
using WindowsAiAssistant.App.Views;

namespace WindowsAiAssistant.App.Services;

public sealed class NavigationService : INavigationService
{
    private Frame? _frame;
    private NavigationView? _navigationView;

    public void Initialize(Frame frame, NavigationView navigationView)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _navigationView = navigationView ?? throw new ArgumentNullException(nameof(navigationView));
    }

    public void Navigate(Type pageType, object? parameter = null)
    {
        if (_frame is null)
        {
            return;
        }

        if (parameter is null)
        {
            _frame.Navigate(pageType);
        }
        else
        {
            _frame.Navigate(pageType, parameter);
        }
    }

    public void NavigateToAssistant() => SelectMenu("Assistant", typeof(AssistantPage));
    public void NavigateToHistory() => SelectMenu("History", typeof(HistoryPage));
    public void NavigateToCapabilities() => SelectMenu("Capabilities", typeof(CapabilitiesPage));
    public void NavigateToSettings() => SelectMenu("Settings", typeof(SettingsPage));

    private void SelectMenu(string tag, Type pageType)
    {
        if (_navigationView is null)
        {
            Navigate(pageType);
            return;
        }

        foreach (var item in _navigationView.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag is string s && s == tag)
            {
                _navigationView.SelectedItem = nvi;
                return;
            }
        }

        Navigate(pageType);
    }
}
