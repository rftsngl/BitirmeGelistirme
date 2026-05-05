using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WindowsAiAssistant.App.ViewModels;

namespace WindowsAiAssistant.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        DataContext = ViewModel;
    }

    public HistoryViewModel ViewModel { get; }
}
