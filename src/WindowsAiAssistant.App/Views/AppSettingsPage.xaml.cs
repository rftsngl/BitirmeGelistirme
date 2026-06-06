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
        Loaded += (_, _) => PorcupineKeyBox.Password = ViewModel.PorcupineAccessKey;
    }

    public AppSettingsViewModel ViewModel { get; }

    private void PorcupineKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            ViewModel.PorcupineAccessKey = box.Password;
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e) => ViewModel.Save();
}
