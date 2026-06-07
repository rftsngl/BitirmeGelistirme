using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAiAssistant.App.ViewModels;

namespace WindowsAiAssistant.App.Views;

public sealed partial class AppSettingsPage : Page
{
    public AppSettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AppSettingsViewModel>();
        DataContext = ViewModel;
    }

    public AppSettingsViewModel ViewModel { get; }

    private void Save_OnClick(object sender, RoutedEventArgs e) => ViewModel.Save();
}
