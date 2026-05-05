using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WindowsAiAssistant.App.ViewModels;

namespace WindowsAiAssistant.App.Views;

public sealed partial class CapabilitiesPage : Page
{
    public CapabilitiesPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<CapabilitiesViewModel>();
        DataContext = ViewModel;
    }

    public CapabilitiesViewModel ViewModel { get; }
}
