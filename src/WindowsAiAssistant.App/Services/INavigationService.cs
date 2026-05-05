namespace WindowsAiAssistant.App.Services;

public interface INavigationService
{
    void Navigate(Type pageType, object? parameter = null);
    void NavigateToAssistant();
    void NavigateToHistory();
    void NavigateToCapabilities();
    void NavigateToSettings();
}
